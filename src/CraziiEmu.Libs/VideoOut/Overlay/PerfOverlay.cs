// Stub for PerfOverlay to satisfy compilation while obeying Rule 8
namespace CraziiEmu.Libs.VideoOut;

public static class PerfOverlay
{
    public const uint PanelWidth = 1;
    public const uint PanelHeight = 1;
    public static bool Enabled => false;
    
    public static void RecordDraw() { }
    public static void RecordPresent() { }
    public static void RecordSubmit() { }
    public static void Toggle() { }
    public static unsafe void Fill(void* pixels, int pendingWork, int pendingGuestSubmissions) { }
}
