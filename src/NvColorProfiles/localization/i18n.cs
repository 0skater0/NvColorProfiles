using System.Globalization;

namespace nv_color_profiles.localization;

/// <summary>
/// Tiny in-process localization: a German/English string table keyed by short ids. UI strings are
/// resolved through <see cref="t"/> (code) or the <c>tr</c> markup extension (XAML). Developer log
/// messages stay English and are NOT in this table.
/// </summary>
public static class i18n
{
    private static string lang = "de";

    public static bool is_english => lang == "en";

    public static void set_language(string code) => lang = code == "en" ? "en" : "de";

    /// <summary>"auto" -> detect from the OS UI culture; an explicit "de"/"en" wins over detection.</summary>
    public static string resolve(string? setting)
    {
        if (string.Equals(setting, "de", StringComparison.OrdinalIgnoreCase))
        {
            return "de";
        }
        if (string.Equals(setting, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en";
        }
        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return ui.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
    }

    /// <summary>Localized string for the active language, or the key itself if it is unknown.</summary>
    public static string t(string key)
        => table.TryGetValue(key, out var pair) ? (lang == "en" ? pair.en : pair.de) : key;

    private static readonly Dictionary<string, (string de, string en)> table = new()
    {
        // common buttons
        ["ok"] = ("OK", "OK"),
        ["cancel"] = ("Abbrechen", "Cancel"),
        ["save"] = ("Speichern", "Save"),
        ["close"] = ("Schließen", "Close"),
        ["new"] = ("Neu", "New"),
        ["edit"] = ("Bearbeiten", "Edit"),
        ["delete"] = ("Löschen", "Delete"),
        ["duplicate"] = ("Duplizieren", "Duplicate"),
        ["rename"] = ("Umbenennen", "Rename"),
        ["change"] = ("Ändern…", "Change…"),
        ["reset_default"] = ("Standard", "Default"),
        ["move_up"] = ("▲ Höher", "▲ Up"),
        ["move_down"] = ("▼ Tiefer", "▼ Down"),

        // settings window — tabs + chrome
        ["settings.title"] = ("NvColorProfiles — Einstellungen", "NvColorProfiles — Settings"),
        ["tab.profiles"] = ("Profile", "Profiles"),
        ["tab.rules"] = ("Regeln", "Rules"),
        ["tab.schedule"] = ("Zeitplan", "Schedule"),
        ["tab.general"] = ("Allgemein", "General"),

        // profile editor
        ["brightness"] = ("Helligkeit", "Brightness"),
        ["contrast"] = ("Kontrast", "Contrast"),
        ["gamma"] = ("Gamma", "Gamma"),
        ["vibrance"] = ("Digitale Farbanpassung", "Digital Vibrance"),
        ["hue"] = ("Farbton", "Hue"),
        ["from_current"] = ("Aus aktuellem Zustand übernehmen", "Adopt from current state"),
        ["reset_monitor"] = ("Monitor zurücksetzen", "Reset monitor"),
        ["all_displays"] = ("Alle Displays", "All displays"),
        ["monitor.all"] = ("Setzt den Basiswert für alle Monitore ohne eigene Einstellung. Wähle oben einen Monitor, um ihm eigene Werte zu geben.",
            "Sets the base value for every monitor without its own setting. Pick a monitor above to give it its own values."),
        ["monitor.all_custom"] = ("Eigene Werte: {0}.", "Custom values: {0}."),
        ["monitor.own"] = ("{0} hat eigene Werte (unabhängig von Alle Displays).",
            "{0} has its own values (independent of All displays)."),
        ["monitor.inherits"] = ("{0} folgt aktuell allen Displays. Verstell die Regler, um ihm eigene Werte zu geben.",
            "{0} currently follows all displays. Adjust the sliders to give it its own values."),
        ["hint.builtin"] = ("Das Default-Profil ist schreibgeschützt. Dupliziere es, um es zu bearbeiten.",
            "The Default profile is read-only. Duplicate it to edit."),
        ["hint.editable"] = ("Änderungen werden sofort als Vorschau angewendet.",
            "Changes are applied immediately as a preview."),
        ["hint.no_gpu"] = ("Keine NVIDIA-GPU erkannt.", "No NVIDIA GPU detected."),
        ["profile.new_default"] = ("Neues Profil", "New profile"),
        ["profile.rename_title"] = ("Profil umbenennen", "Rename profile"),
        ["profile.copy_suffix"] = (" Kopie", " Copy"),
        ["profile.delete_title"] = ("Profil löschen", "Delete profile"),
        ["profile.delete_confirm"] = ("Profil \"{0}\" wirklich löschen?", "Delete profile \"{0}\"?"),

        // rules tab
        ["fallback_label"] = ("Fallback-Profil:", "Fallback profile:"),
        ["delay_label"] = ("Umschalt-Verzögerung:", "Switch delay:"),
        ["direct_entry_tip"] = ("Strg+Klick: Wert direkt eingeben", "Ctrl+Click: enter a value directly"),
        ["enter_value"] = ("Wert eingeben", "Enter value"),
        ["delay_prompt"] = ("Umschalt-Verzögerung (Sekunden)", "Switch delay (seconds)"),
        ["rules_hint"] = (
            "Regeln greifen im Auto-Modus (Tray → \"Automatisch (Regeln)\"). Erste passende Regel gewinnt. Verzögerung: wie lange ein Fenster fokussiert sein muss, bevor umgeschaltet wird.",
            "Rules apply in auto mode (tray → \"Automatic (rules)\"). The first matching rule wins. Delay: how long a window must stay focused before it switches."),

        // schedule tab
        ["schedule_hint"] = (
            "Zeitpläne greifen im Auto-Modus, wenn keine App-Regel passt. Erster passender Eintrag gewinnt. Fenster über Mitternacht (z. B. 22:00–06:00) sind erlaubt.",
            "Schedules apply in auto mode when no app rule matches. The first matching entry wins. Windows that wrap past midnight (e.g. 22:00–06:00) are allowed."),

        // general tab
        ["autostart"] = ("Mit Windows starten (Autostart)", "Start with Windows (autostart)"),
        ["restore_on_exit"] = ("Beim Beenden Farben auf den Ausgangszustand zurücksetzen",
            "Restore colors to the original state on exit"),
        ["diagnostic"] = ("Diagnose-Logging (ausführlich)", "Diagnostic logging (verbose)"),
        ["hotkeys_enabled"] = ("Globale Hotkeys aktiv", "Global hotkeys enabled"),
        ["hk_next"] = ("Nächstes Profil", "Next profile"),
        ["hk_prev"] = ("Vorheriges Profil", "Previous profile"),
        ["hk_toggle"] = ("Auto-Modus umschalten", "Toggle auto mode"),
        ["backup"] = ("Sicherung", "Backup"),
        ["export"] = ("Profile exportieren…", "Export profiles…"),
        ["import"] = ("Profile importieren…", "Import profiles…"),
        ["backup_hint"] = ("Exportiert Profile und Regeln als JSON-Datei. Beim Import bleiben lokale Einstellungen (Autostart usw.) erhalten.",
            "Exports profiles and rules as a JSON file. On import, local settings (autostart etc.) are kept."),
        ["language_label"] = ("Sprache", "Language"),
        ["language.auto"] = ("Automatisch", "Automatic"),
        ["licenses"] = ("Lizenzen", "Licenses"),
        ["licenses.title"] = ("Lizenzen und Drittanbieter-Hinweise", "Licenses and third-party notices"),

        // export/import status
        ["config_location"] = ("Konfiguration: {0}", "Configuration: {0}"),
        ["saved"] = ("Gespeichert ✓", "Saved ✓"),
        ["export.title"] = ("Profile exportieren", "Export profiles"),
        ["import.title"] = ("Profile importieren", "Import profiles"),
        ["export.done"] = ("Exportiert nach {0}.", "Exported to {0}."),
        ["export.failed"] = ("Export fehlgeschlagen: {0}", "Export failed: {0}"),
        ["import.failed"] = ("Import fehlgeschlagen: {0}", "Import failed: {0}"),
        ["import.invalid"] = ("Import fehlgeschlagen: keine gültige Profil-Datei.",
            "Import failed: not a valid profile file."),
        ["import.done"] = ("Importiert: {0} Profile, {1} Regeln, {2} Zeitpläne.",
            "Imported: {0} profiles, {1} rules, {2} schedules."),
        ["import.active_reset"] = (" Aktives Profil zurückgesetzt.", " Active profile reset."),

        // rule editor
        ["condition"] = ("Bedingung", "Condition"),
        ["value"] = ("Wert", "Value"),
        ["profile"] = ("Profil", "Profile"),
        ["pick_running_app"] = ("Aus laufender App wählen…", "Pick from a running app…"),
        ["match.process"] = ("Prozess (z. B. game.exe)", "Process (e.g. game.exe)"),
        ["match.title"] = ("Fenstertitel (Regex)", "Window title (regex)"),
        ["kind.process"] = ("Prozess", "Process"),
        ["kind.title"] = ("Fenstertitel", "Window title"),
        ["rule.new"] = ("Neue Regel", "New rule"),
        ["rule.edit"] = ("Regel bearbeiten", "Edit rule"),

        // schedule editor
        ["from"] = ("Von", "From"),
        ["to"] = ("Bis", "To"),
        ["schedule.new"] = ("Neuer Zeitplan", "New schedule"),
        ["schedule.edit"] = ("Zeitplan bearbeiten", "Edit schedule"),

        // hotkey capture
        ["hotkey.title"] = ("Hotkey festlegen", "Set hotkey"),
        ["hotkey.prompt"] = ("Drücke die gewünschte Tastenkombination (mindestens mit Strg, Alt oder Win).",
            "Press the desired key combination (with at least Ctrl, Alt or Win)."),
        ["hotkey.need_mod"] = ("Mindestens Strg, Alt oder Win zusätzlich drücken.",
            "Also hold at least Ctrl, Alt or Win."),
        ["hotkey.unsupported"] = ("Diese Taste wird nicht unterstützt.", "This key is not supported."),

        // tray
        ["tray.no_gpu"] = ("Keine NVIDIA-GPU gefunden", "No NVIDIA GPU found"),
        ["tray.auto"] = ("Automatisch (Regeln)", "Automatic (rules)"),
        ["tray.reset"] = ("Farben auf Standard zurücksetzen", "Reset colors to default"),
        ["tray.settings"] = ("Einstellungen…", "Settings…"),
        ["tray.exit"] = ("Beenden", "Exit"),
        ["tray.tooltip_no_gpu"] = ("NvColorProfiles — keine NVIDIA-GPU", "NvColorProfiles — no NVIDIA GPU"),
    };
}
