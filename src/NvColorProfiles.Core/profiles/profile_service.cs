using Microsoft.Extensions.Logging;
using nv_color_profiles.core.display;

namespace nv_color_profiles.core.profiles;

/// <summary>
/// Applies a <see cref="profile"/> across every connected display and tracks which profile is
/// currently active. The per-display resolution (exact id → wildcard → neutral) lives in
/// <see cref="profile.settings_for"/>.
/// </summary>
public sealed class profile_service
{
    private readonly display_catalog catalog;
    private readonly display_controller controller;
    private readonly ILogger<profile_service> log;

    public profile_service(display_catalog catalog, display_controller controller, ILogger<profile_service> log)
    {
        this.catalog = catalog;
        this.controller = controller;
        this.log = log;
    }

    /// <summary>Name of the profile applied last, or null if none has been applied yet.</summary>
    public string? active_profile_name { get; private set; }

    /// <summary>Applies the profile to all displays and records it as active.</summary>
    public void apply(profile target)
    {
        var displays = catalog.get_displays();
        foreach (var display in displays)
        {
            // defensive: one display failing must never block the others
            try
            {
                controller.apply(target.settings_for(display.display_id), display);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to apply profile to display {name}", display.gdi_name);
            }
        }

        active_profile_name = target.name;
        log.LogInformation("Applied profile '{name}' to {count} display(s)", target.name, displays.Count);
    }
}
