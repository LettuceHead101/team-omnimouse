using System;

namespace OmniMouse.Network
{
    public class LayoutChangedEventArgs : EventArgs
    {
        public SessionLayout Layout { get; }
        public string Reason { get; }

        public LayoutChangedEventArgs(SessionLayout layout, string reason)
        {
            Layout = layout;
            Reason = reason;
        }
    }
}
