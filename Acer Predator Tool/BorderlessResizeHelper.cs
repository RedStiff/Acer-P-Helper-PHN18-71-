namespace PredatorControlApp
{
    internal static class BorderlessResizeHelper
    {
        public const int WmNchitTest = 0x0084;

        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        private const int ResizeBorder = 12;

        public static void ApplyTo(Form form, int minWidth = 400, int minHeight = 320)
        {
            form.MinimumSize = new Size(minWidth, minHeight);
            form.MaximumSize = new Size(0, 0);
        }

        /// <summary>
        /// Edge hit-test before child controls. Returns true when m.Result was set to a sizing HT*.
        /// </summary>
        public static bool TryEdgeHit(Form form, ref Message m)
        {
            Point client = form.PointToClient(Cursor.Position);

            int w = form.ClientSize.Width;
            int h = form.ClientSize.Height;
            if (w <= 0 || h <= 0) return false;

            bool onLeft = client.X < ResizeBorder;
            bool onRight = client.X >= w - ResizeBorder;
            bool onTop = client.Y < ResizeBorder;
            bool onBottom = client.Y >= h - ResizeBorder;

            if (!onLeft && !onRight && !onTop && !onBottom)
                return false;

            if (onTop && onLeft) { m.Result = (IntPtr)HtTopLeft; return true; }
            if (onTop && onRight) { m.Result = (IntPtr)HtTopRight; return true; }
            if (onBottom && onLeft) { m.Result = (IntPtr)HtBottomLeft; return true; }
            if (onBottom && onRight) { m.Result = (IntPtr)HtBottomRight; return true; }
            if (onLeft) { m.Result = (IntPtr)HtLeft; return true; }
            if (onRight) { m.Result = (IntPtr)HtRight; return true; }
            if (onTop) { m.Result = (IntPtr)HtTop; return true; }
            m.Result = (IntPtr)HtBottom;
            return true;
        }
    }
}
