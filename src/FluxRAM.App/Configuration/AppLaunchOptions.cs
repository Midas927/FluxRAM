namespace FluxRAM.App.Configuration;

public static class AppLaunchOptions
{
    public static bool IsUiPreview(IReadOnlyList<string> args)
    {
        return args.Any(argument =>
            string.Equals(argument, "--ui-preview", StringComparison.OrdinalIgnoreCase));
    }
}
