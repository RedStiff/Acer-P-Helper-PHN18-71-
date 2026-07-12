namespace PredatorControlApp
{
    /// <summary>
    /// Reveals the right-side curves panel by growing the form width in place.
    /// Lighting is a separate attached window and is not handled here.
    /// </summary>
    internal sealed class ShellPanelAnimator : IDisposable
    {
        private readonly Form _form;
        private readonly Action<int, int> _layoutPanels;
        private readonly int _mainWidth;
        private readonly int _mainHeight;
        private readonly int _rightTargetWidth;

        private int _rightReveal;

        public ShellPanelAnimator(
            Form form,
            int mainWidth,
            int mainHeight,
            int rightTargetWidth,
            Action<int, int> layoutPanels)
        {
            _form = form;
            _mainWidth = mainWidth;
            _mainHeight = mainHeight;
            _rightTargetWidth = rightTargetWidth;
            _layoutPanels = layoutPanels;
        }

        public int RightPanelWidth => _rightReveal;
        public bool IsAnimating => false;
        public bool IsRightOpen => _rightReveal >= _rightTargetWidth;

        public void ApplyImmediate(int rightReveal)
        {
            _rightReveal = rightReveal;
            _form.SuspendLayout();
            try
            {
                _layoutPanels(_mainWidth, rightReveal);
                _form.ClientSize = new Size(_mainWidth + rightReveal, _mainHeight);
            }
            finally
            {
                _form.ResumeLayout(true);
            }
        }

        public void AnimateRight(bool open, Action? onComplete = null)
        {
            ApplyImmediate(open ? _rightTargetWidth : 0);
            onComplete?.Invoke();
        }

        public void Dispose() { }
    }
}
