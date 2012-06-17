using System.Diagnostics;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace AgFx
{
    public static class ThreadingHelper
    {
        private static bool _initialized;
        public static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            Dispatcher = Window.Current.Dispatcher;
            Debug.Assert(Dispatcher != null);

            _initialized = true;
        }

        public static CoreDispatcher Dispatcher { get; private set; }
    }
}
