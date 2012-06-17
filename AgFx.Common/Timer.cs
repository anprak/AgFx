using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace AgFx
{
    public sealed class Timer : IDisposable
    {
        private readonly System.Threading.Timer _timer;

        public Timer(Action callback, object state, int dueTime, int period)
        {
            _timer = new System.Threading.Timer(s => callback(), state, dueTime, period);
        }

        public Timer(Action callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = new System.Threading.Timer(s => callback(), state, dueTime, period);
        }

        public bool Change(int dueTime, int period)
        {
            return _timer.Change(dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return _timer.Change(dueTime, period);
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
