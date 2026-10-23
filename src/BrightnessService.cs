using System;
using System.Collections.Generic;
using System.Threading;

namespace Jasnosc
{
    // Cała komunikacja z monitorami idzie przez jeden wątek w tle, więc:
    //  - UI nigdy się nie zacina, nawet gdy monitor długo odpowiada,
    //  - komendy DDC nie nakładają się na siebie,
    //  - przy przeciąganiu suwaka wysyłana jest tylko najnowsza wartość, a nie kolejka setek zmian.
    class BrightnessService : IDisposable
    {
        readonly object sync = new object();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Dictionary<string, int> pending = new Dictionary<string, int>();
        readonly Thread worker;
        List<Display> displays = new List<Display>();
        bool refreshRequested;
        bool stopping;
        Timer delayedRefresh;

        // Wywoływane z wątku roboczego.
        public event Action<DisplayInfo[]> Changed;

        public BrightnessService()
        {
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "Jasnosc worker";
            worker.Start();
        }

        public void RequestRefresh()
        {
            lock (sync) refreshRequested = true;
            signal.Set();
        }

        // Po wybudzeniu albo podłączeniu monitora DDC/CI potrzebuje chwili, zanim zacznie odpowiadać.
        public void RequestRefresh(int delayMs)
        {
            lock (sync)
            {
                if (stopping) return;
                if (delayedRefresh != null) delayedRefresh.Dispose();
                delayedRefresh = new Timer(delegate { RequestRefresh(); }, null, delayMs, Timeout.Infinite);
            }
        }

        public void RequestSet(string id, int percent)
        {
            lock (sync) pending[id] = percent;
            signal.Set();
        }

        void Loop()
        {
            while (true)
            {
                signal.WaitOne();
                while (true)
                {
                    bool refresh;
                    List<KeyValuePair<string, int>> sets;
                    lock (sync)
                    {
                        if (stopping)
                        {
                            DisposeDisplays();
                            return;
                        }
                        refresh = refreshRequested;
                        refreshRequested = false;
                        sets = new List<KeyValuePair<string, int>>(pending);
                        pending.Clear();
                    }
                    if (!refresh && sets.Count == 0) break;

                    try
                    {
                        if (refresh) Rescan();
                        bool failed = !Apply(sets);
                        if (failed)
                        {
                            // Najczęstsza przyczyna: nieaktualne uchwyty po uśpieniu lub przepięciu kabla.
                            Rescan();
                            Apply(sets);
                        }
                        if (refresh || failed) Publish();
                    }
                    catch (Exception)
                    {
                        // Wątek musi przeżyć każdy błąd sterownika - przy kolejnej akcji spróbujemy ponownie.
                    }
                }
            }
        }

        bool Apply(List<KeyValuePair<string, int>> sets)
        {
            bool allOk = true;
            foreach (KeyValuePair<string, int> s in sets)
            {
                Display d = displays.Find(x => x.Id == s.Key);
                if (d == null || !d.Available) continue;
                if (!d.Set(s.Value)) allOk = false;
            }
            return allOk;
        }

        void Rescan()
        {
            DisposeDisplays();
            displays = DisplayScanner.Scan();
        }

        void Publish()
        {
            Action<DisplayInfo[]> handler = Changed;
            if (handler == null) return;
            handler(displays.ConvertAll(d => d.Snapshot()).ToArray());
        }

        void DisposeDisplays()
        {
            foreach (Display d in displays) d.Dispose();
            displays = new List<Display>();
        }

        public void Dispose()
        {
            lock (sync)
            {
                stopping = true;
                if (delayedRefresh != null) delayedRefresh.Dispose();
            }
            signal.Set();
            worker.Join(3000);
        }
    }
}
