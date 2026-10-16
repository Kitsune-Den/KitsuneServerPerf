using System.Collections.Generic;
using System.Globalization;

namespace KitsuneServerPerf
{
    public class PerfCommand : ConsoleCmdAbstract
    {
        public override string[] getCommands() => new[] { "kperf" };

        public override string getDescription() => "Kitsune Server Perf: time the server's hot code and every mod's patches.";

        public override string getHelp() =>
            "Usage:\n" +
            "  kperf start [seconds]  time for N seconds (default 30), then print the report\n" +
            "  kperf stop             stop early and print the report\n" +
            "  kperf report           print the last report again\n" +
            "  kperf census           what's alive right now and how fast its AI is running\n" +
            "Profile with players online and zombies around: an empty server pauses most of its work.\n" +
            "Timing adds some overhead while it runs; nothing is changed once it stops.";

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "help";
            switch (sub)
            {
                case "start":
                    float seconds = 30f;
                    if (_params.Count > 1 && (!float.TryParse(_params[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 1f || seconds > 600f))
                    {
                        Print("Seconds must be a number from 1 to 600.");
                        return;
                    }
                    Print(Profiler.Start(seconds));
                    break;
                case "stop":
                    Print(Profiler.Stop(printReport: false));
                    break;
                case "report":
                    Print(Profiler.LastReport ?? "No report yet. Run 'kperf start' first.");
                    break;
                case "census":
                    Print(Profiler.Census());
                    Print("Most common: " + Profiler.TopClasses(15));
                    break;
                default:
                    Print(getHelp());
                    break;
            }
        }

        private static void Print(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output(line);
            }
        }
    }
}
