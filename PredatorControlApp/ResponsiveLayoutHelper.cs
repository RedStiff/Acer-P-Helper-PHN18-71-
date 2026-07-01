namespace PredatorControlApp
{
    /// <summary>
    /// Scales child control bounds proportionally when the host form is resized.
    /// Bounds are stored as fractions of each parent&apos;s client area at snapshot time.
    /// </summary>
    internal sealed class ResponsiveLayoutHelper
    {
        private sealed record Entry(Control Control, RectangleF Fraction);

        private readonly Form _form;
        private readonly Size _designClientSize;
        private readonly List<Entry> _entries = [];
        private bool _updating;

        public ResponsiveLayoutHelper(Form form, Size designClientSize)
        {
            _form = form;
            _designClientSize = designClientSize;
            _form.Resize += (_, _) => Apply();
            _form.ResizeEnd += (_, _) => Apply();
        }

        public void Snapshot(Control root)
        {
            _entries.Clear();
            Capture(root);
        }

        private void Capture(Control parent)
        {
            int pw = Math.Max(1, parent.ClientSize.Width);
            int ph = Math.Max(1, parent.ClientSize.Height);

            foreach (Control child in parent.Controls)
            {
                if (child.Dock != DockStyle.None)
                    continue;

                _entries.Add(new Entry(child, new RectangleF(
                    child.Left / (float)pw,
                    child.Top / (float)ph,
                    child.Width / (float)pw,
                    child.Height / (float)ph)));

                Capture(child);
            }
        }

        public void Apply()
        {
            if (_updating || _entries.Count == 0 || _form.WindowState == FormWindowState.Minimized)
                return;

            if (_form.ClientSize.Width < 1 || _form.ClientSize.Height < 1)
                return;

            _updating = true;
            try
            {
                _form.SuspendLayout();
                foreach (var entry in _entries)
                {
                    var parent = entry.Control.Parent;
                    if (parent == null || parent.IsDisposed || entry.Control.IsDisposed)
                        continue;

                    int pw = Math.Max(1, parent.ClientSize.Width);
                    int ph = Math.Max(1, parent.ClientSize.Height);
                    var f = entry.Fraction;

                    entry.Control.SetBounds(
                        (int)(f.X * pw),
                        (int)(f.Y * ph),
                        Math.Max(1, (int)(f.Width * pw)),
                        Math.Max(1, (int)(f.Height * ph)));
                }
                _form.ResumeLayout(true);
                _form.Invalidate(true);
            }
            finally
            {
                _updating = false;
            }
        }

        public Size DesignClientSize => _designClientSize;
    }
}
