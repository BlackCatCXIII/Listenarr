namespace Listenarr.Application.Configuration.Core
{
    public partial class ConfigurationService
    {
        private static void ApplyEnvironmentOverrides(ApplicationSettings settings)
        {
            ApplyBooleanEnvironmentOverride(
                "LISTENARR_EXPORT_COVER_SIDECARS",
                value => settings.ExportCoverSidecars = value);
            ApplyBooleanEnvironmentOverride(
                "LISTENARR_OVERWRITE_MANAGED_COVER_SIDECARS",
                value => settings.OverwriteManagedCoverSidecars = value);

            var sidecarFileName = Environment.GetEnvironmentVariable("LISTENARR_COVER_SIDECAR_FILE_NAME");
            if (!string.IsNullOrWhiteSpace(sidecarFileName))
            {
                settings.CoverSidecarFileName = sidecarFileName.Trim();
            }
        }

        private static void ApplyBooleanEnvironmentOverride(string name, Action<bool> apply)
        {
            var rawValue = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            if (bool.TryParse(rawValue, out var parsed))
            {
                apply(parsed);
                return;
            }

            if (string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue, "on", StringComparison.OrdinalIgnoreCase))
            {
                apply(true);
                return;
            }

            if (string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawValue, "off", StringComparison.OrdinalIgnoreCase))
            {
                apply(false);
            }
        }
    }
}
