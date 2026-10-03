using System;
using System.Collections.Generic;
using UnityEngine;

namespace FumoMP
{
    /// <summary>
    /// Live standings.
    ///
    /// Ranked on the same progress the game itself ranks with: the lap, then the
    /// trigger counter the circuit increments as you pass its zones, then the
    /// trigger index, then the elapsed time. Both counters travel in the kart
    /// packet (protocol 4), so every machine can order the whole field without
    /// anyone owning the race state - which matters here because this build cannot
    /// spawn a real local racer per player (that is the path that hangs the start
    /// sequence), so the game's own ranking never sees the remote karts.
    ///
    /// The gap column is a physical distance to the kart ahead: exact time gaps
    /// would need a shared clock, and each machine measures only its own kart's
    /// time.
    /// </summary>
    internal static class MpRank
    {
        internal class Entry
        {
            internal int Slot = -1;
            internal string Name = "";
            internal int Lap, Trigger, TriggerIndex;
            internal float TotalTime;
            internal bool Finished, Local;
            internal Vector3 Pos;
            internal float GapMeters;      // distance to the entry ahead
        }

        private static readonly List<Entry> Rows = new List<Entry>();
        private static float _nextCompute;
        private static int _localPosition;

        internal static List<Entry> Standings { get { return Rows; } }
        internal static int LocalPosition { get { return _localPosition; } }
        internal static int Count { get { return Rows.Count; } }

        internal static void Tick(float dt)
        {
            try
            {
                _nextCompute -= dt;
                if (_nextCompute > 0f) return;
                _nextCompute = 0.25f;
                Recompute();
            }
            catch (Exception e) { Plugin.Log.LogWarning("rank tick: " + e.Message); }
        }

        private static void Recompute()
        {
            Rows.Clear();
            _localPosition = 0;

            // this machine's kart
            var local = MpDiag.LocalRacer();
            if (local != null)
            {
                var e = new Entry { Local = true, Name = MpFlow.Nickname, Slot = MpNet.OwnSlot };
                try { e.Lap = local._curLap; } catch { }
                try { e.Trigger = (int)MpDiag.Num(local, "_curTrigger"); } catch { }
                try { e.TriggerIndex = (int)MpDiag.Num(local, "_curTriggerIndex"); } catch { }
                try { e.TotalTime = (float)local.GetTotalTime().TotalSeconds; } catch { }
                try { e.Finished = local._endedRace; } catch { }
                try { e.Pos = local.transform.position; } catch { }
                if (string.IsNullOrEmpty(e.Name)) e.Name = "You";
                Rows.Add(e);
            }

            // every other machine, as the kart channel reports it
            if (MpNet.Running)
                foreach (var p in MpNet.Peers)
                {
                    var k = MpNet.Kart(p.Slot);
                    var e = new Entry { Slot = p.Slot };
                    e.Name = !string.IsNullOrEmpty(p.Nick) ? p.Nick : (k != null ? k.Nick : "Player" + p.Slot);
                    if (string.IsNullOrEmpty(e.Name)) e.Name = "Player" + p.Slot;
                    if (k != null)
                    {
                        e.Lap = k.Lap; e.Trigger = k.Trigger; e.TriggerIndex = k.TriggerIndex;
                        e.TotalTime = k.TotalTime; e.Finished = k.Finished; e.Pos = k.Pos;
                    }
                    Rows.Add(e);
                }

            Rows.Sort(Compare);

            for (int i = 1; i < Rows.Count; i++)
                Rows[i].GapMeters = Vector3.Distance(Rows[i].Pos, Rows[i - 1].Pos);
            if (Rows.Count > 0) Rows[0].GapMeters = 0f;

            for (int i = 0; i < Rows.Count; i++)
                if (Rows[i].Local) _localPosition = i + 1;
        }

        private static int Compare(Entry a, Entry b)
        {
            // finished karts first, fastest first
            if (a.Finished != b.Finished) return a.Finished ? -1 : 1;
            if (a.Finished && b.Finished)
            {
                if (a.TotalTime > 0.05f && b.TotalTime > 0.05f) return a.TotalTime.CompareTo(b.TotalTime);
                return 0;
            }
            if (a.Lap != b.Lap) return b.Lap.CompareTo(a.Lap);
            if (a.Trigger != b.Trigger) return b.Trigger.CompareTo(a.Trigger);
            if (a.TriggerIndex != b.TriggerIndex) return b.TriggerIndex.CompareTo(a.TriggerIndex);
            // same lap and same zone: whoever is closer to the next one leads
            return Vector3.Distance(a.Pos, b.Pos).CompareTo(0f) * 0 + a.TotalTime.CompareTo(b.TotalTime);
        }

        /// <summary>One line per kart, for the in-race board (fixed width columns).</summary>
        internal static string Board()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Rows.Count && i < MpNet.MaxPlayers; i++)
            {
                var e = Rows[i];
                string name = e.Name;
                if (name.Length > 12) name = name.Substring(0, 12);
                string gap = i == 0 ? "" : (e.GapMeters >= 1f ? ("+" + e.GapMeters.ToString("F0") + "m") : "+0m");
                string mark = e.Local ? ">" : " ";
                string state = e.Finished ? "FIN" : ("L" + (e.Lap + 1));
                if (e.Local)
                    sb.Append("<color=#FFE066>").Append(mark).Append(i + 1).Append("  ")
                      .Append(name.PadRight(13)).Append(state.PadRight(5)).Append(gap).Append("</color>");
                else
                    sb.Append("<color=#C8CFDE>").Append(mark).Append(i + 1).Append("  ")
                      .Append(name.PadRight(13)).Append(state.PadRight(5)).Append(gap).Append("</color>");
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }
    }
}
