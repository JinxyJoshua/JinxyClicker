using System;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>Which graphics backend to suggest, and the reason for it.</summary>
public sealed record ApiSuggestion(string? Api, string Reason);

/// <summary>
/// Identifies the display adapter and whether Vulkan can actually run on it.
/// </summary>
public static class GpuInfo
{
    /// <summary>The primary adapter's name, or null if it cannot be read.</summary>
    public static string? AdapterName()
    {
        try
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

            return EnumDisplayDevices(null, 0, ref device, 0) ? device.DeviceString : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a Vulkan instance can be created and reports at least one device.
    /// </summary>
    /// <remarks>
    /// Actually initialises Vulkan rather than looking for driver registrations.
    /// The registry route is unreliable: measured on an AMD laptop with a fully
    /// working Vulkan stack, HKLM\SOFTWARE\Khronos\Vulkan\Drivers did not exist
    /// at all, while vulkaninfo enumerated the GPU without complaint. Believing
    /// the registry there would have ruled out the backend most likely to be the
    /// fastest one on that machine.
    ///
    /// The presence of vulkan-1.dll is not sufficient either — it ships with
    /// Windows and loads happily with no driver behind it.
    /// </remarks>
    public static bool VulkanAvailable()
    {
        IntPtr instance = IntPtr.Zero;

        try
        {
            var info = new VkInstanceCreateInfo { sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO };

            if (vkCreateInstance(ref info, IntPtr.Zero, out instance) != VK_SUCCESS) return false;

            uint count = 0;

            // Null buffer asks only for the count, which is all that matters here.
            return vkEnumeratePhysicalDevices(instance, ref count, IntPtr.Zero) == VK_SUCCESS && count > 0;
        }
        catch
        {
            // No runtime, or a loader that refuses to initialise.
            return false;
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                try { vkDestroyInstance(instance, IntPtr.Zero); } catch { }
            }
        }
    }

    /// <summary>
    /// A starting point for the graphics backend, based on the adapter vendor.
    /// </summary>
    /// <remarks>
    /// A heuristic, and it says so where it is shown. Which backend is fastest
    /// depends on the driver revision and the scene as much as the vendor, and
    /// the only way to actually know is to run each and compare. What this can
    /// do honestly is avoid the clearly wrong answers: never suggest Vulkan on a
    /// machine that cannot run it, and prefer the backend each vendor's driver
    /// has historically handled best.
    /// </remarks>
    public static ApiSuggestion Recommend()
    {
        string? adapter = AdapterName();

        if (adapter == null)
            return new ApiSuggestion(null, "Could not identify the display adapter, so this leaves the choice to the client.");

        bool vulkan = VulkanAvailable();
        string name = adapter.Trim();

        if (adapter.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiSuggestion("D3D11",
                $"{name}. Direct3D 11 is the better-trodden path on NVIDIA drivers. " +
                (vulkan ? "Vulkan works here too and is worth trying second." : "Vulkan is not available on this machine."));
        }

        if (adapter.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || adapter.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return vulkan
                ? new ApiSuggestion("Vulkan",
                    $"{name}. AMD's Vulkan driver is strong, and Vulkan usually leads on Radeon hardware. Compare against D3D11 before settling.")
                : new ApiSuggestion("D3D11",
                    $"{name}. Vulkan could not be initialised on this machine, so Direct3D 11 it is.");
        }

        if (adapter.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return new ApiSuggestion("D3D11",
                $"{name}. Direct3D 11 is the steadier choice on Intel graphics. " +
                (vulkan ? "Vulkan is available if you want to compare." : "Vulkan is not available on this machine."));
        }

        return new ApiSuggestion(null,
            $"{name}. Not a vendor with a clear default, so this leaves the choice to the client. Try each and compare.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? device, uint index, ref DISPLAY_DEVICE info, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    private const int VK_SUCCESS = 0;
    private const uint VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct VkInstanceCreateInfo
    {
        public uint sType;
        public IntPtr pNext;
        public uint flags;
        public IntPtr pApplicationInfo;
        public uint enabledLayerCount;
        public IntPtr ppEnabledLayerNames;
        public uint enabledExtensionCount;
        public IntPtr ppEnabledExtensionNames;
    }

    [DllImport("vulkan-1.dll")]
    private static extern int vkCreateInstance(
        ref VkInstanceCreateInfo info, IntPtr allocator, out IntPtr instance);

    [DllImport("vulkan-1.dll")]
    private static extern int vkEnumeratePhysicalDevices(
        IntPtr instance, ref uint count, IntPtr devices);

    [DllImport("vulkan-1.dll")]
    private static extern void vkDestroyInstance(IntPtr instance, IntPtr allocator);
}
