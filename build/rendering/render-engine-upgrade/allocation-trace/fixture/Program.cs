using System.Runtime.CompilerServices;
static class AllocationFixture
{
    private static object? sink;
    [MethodImpl(MethodImplOptions.NoInlining)] private static void AllocateA() => Volatile.Write(ref sink, new byte[16384]);
    [MethodImpl(MethodImplOptions.NoInlining)] private static void AllocateB() => Volatile.Write(ref sink, new byte[32768]);
    public static void Main(string[] args)
    {
        Console.WriteLine(Environment.ProcessId);
        if (args.Length == 1) { using var file = new StreamWriter(new FileStream(args[0], FileMode.CreateNew)); file.Write(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        for (int i = 0; i < 512; i++) { AllocateA(); AllocateB(); Thread.Sleep(1); }
        GC.KeepAlive(sink);
    }
}
