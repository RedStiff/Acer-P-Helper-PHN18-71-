using System.Runtime.Versioning;

namespace PredatorControlApp
{
    /// <summary>Borderless form without resize grips — fixed client layout.</summary>
    [SupportedOSPlatform("windows")]
    public class BorderlessForm : Form
    {
        public const int WmEnterSizeMove = 0x0231;
        public const int WmExitSizeMove = 0x0232;

        public event EventHandler? EnterSizeMove;
        public event EventHandler? ExitSizeMove;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmEnterSizeMove)
                EnterSizeMove?.Invoke(this, EventArgs.Empty);
            else if (m.Msg == WmExitSizeMove)
                ExitSizeMove?.Invoke(this, EventArgs.Empty);

            base.WndProc(ref m);
        }
    }
}
