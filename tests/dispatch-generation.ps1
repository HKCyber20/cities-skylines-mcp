$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $repo 'mod\Dispatch.cs') -Raw
$stubs = @'
namespace ColossalFramework {
    public static class Singleton<T> where T : new() { public static readonly T instance = new T(); }
}
public sealed class SimulationManager {
    public readonly System.Collections.Generic.Queue<System.Action> Actions =
        new System.Collections.Generic.Queue<System.Action>();
    public readonly System.Threading.ManualResetEvent Queued = new System.Threading.ManualResetEvent(false);
    public void AddAction(System.Action action) {
        lock (Actions) Actions.Enqueue(action);
        Queued.Set();
    }
    public void Pump() {
        System.Action action;
        lock (Actions) action = Actions.Dequeue();
        action();
    }
}
namespace CS1McpBridge {
    internal static class Log { public static void Error(object value) { throw new System.Exception(value.ToString()); } }
    public static class GenerationTests {
        static int _executed;
        static void WaitForMainQueue() {
            var type = typeof(Dispatch);
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var queue = (System.Collections.Generic.Queue<System.Action>)type.GetField("_mainQueue", flags).GetValue(null);
            var gate = type.GetField("_gate", flags).GetValue(null);
            for (int i = 0; i < 100; i++) {
                lock (gate) if (queue.Count > 0) return;
                System.Threading.Thread.Sleep(10);
            }
            throw new System.Exception("main work was not queued");
        }
        static void ExpectCancelled(System.Threading.Tasks.Task<string> task) {
            if (!task.Wait(3000) || task.Result != "cancelled")
                throw new System.Exception("pending work was not cancelled");
        }
        public static string Run() {
            var old = Dispatch.CurrentGeneration;
            var main = System.Threading.Tasks.Task.Run(() => {
                try { Dispatch.Run(RunOn.Main, () => { _executed++; return 1; }); return "ran"; }
                catch (System.OperationCanceledException) { return "cancelled"; }
            });
            WaitForMainQueue();
            Dispatch.InvalidatePending();
            Dispatch.PumpMainThread();
            ExpectCancelled(main);
            if (_executed != 0) throw new System.Exception("old main work ran");

            var sim = System.Threading.Tasks.Task.Run(() => {
                try { Dispatch.Run(RunOn.Sim, () => { _executed++; return 1; }); return "ran"; }
                catch (System.OperationCanceledException) { return "cancelled"; }
            });
            var manager = ColossalFramework.Singleton<SimulationManager>.instance;
            if (!manager.Queued.WaitOne(3000)) throw new System.Exception("sim work was not queued");
            Dispatch.InvalidatePending();
            manager.Pump();
            ExpectCancelled(sim);
            if (_executed != 0) throw new System.Exception("old sim work ran");

            Dispatch.BindRequest(old);
            try {
                try { Dispatch.Run(RunOn.Main, () => { _executed++; return 1; });
                      throw new System.Exception("old request accepted"); }
                catch (System.OperationCanceledException) { }
            } finally { Dispatch.UnbindRequest(); }

            var fresh = System.Threading.Tasks.Task.Run(() =>
                Dispatch.Run(RunOn.Main, () => { _executed++; return 42; }));
            WaitForMainQueue();
            Dispatch.PumpMainThread();
            if (!fresh.Wait(3000) || fresh.Result != 42 || _executed != 1)
                throw new System.Exception("new generation did not execute");
            return "old main/sim requests cancelled; old connection rejected; new generation executed";
        }
    }
}
'@
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $stubs)
[CS1McpBridge.GenerationTests]::Run()
