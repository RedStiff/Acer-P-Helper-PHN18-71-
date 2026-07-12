using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>
    /// Borderless form that can still be resized from edges (requires WS_THICKFRAME).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ResizableBorderlessForm : Form
    {
        private const int WsThickFrame = 0x00040000;
        public const int WmEnterSizeMove = 0x0231;
        public const int WmExitSizeMove = 0x0232;

        public event EventHandler? EnterSizeMove;
        public event EventHandler? ExitSizeMove;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (FormBorderStyle == FormBorderStyle.None)
                    cp.Style |= WsThickFrame;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmEnterSizeMove)
                EnterSizeMove?.Invoke(this, EventArgs.Empty);
            else if (m.Msg == WmExitSizeMove)
                ExitSizeMove?.Invoke(this, EventArgs.Empty);

            if (m.Msg == BorderlessResizeHelper.WmNchitTest)
            {
                if (!BorderlessResizeHelper.TryEdgeHit(this, ref m))
                    base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
