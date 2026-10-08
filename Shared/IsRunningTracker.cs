using System;
using System.Collections.Generic;
using System.Text;

namespace Shared
{
    public class IsRunningTracker
    {
        public bool IsRunning { get; set; } = false;

        public IsRunningTracker(bool running = false) {
            IsRunning = running;
        }
    }
}
