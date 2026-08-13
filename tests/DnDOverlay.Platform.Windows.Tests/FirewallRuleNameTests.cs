using DnDOverlay.Firewall;

namespace DnDOverlay.Platform.Windows.Tests;

/// <summary>
/// How the helpers name the rule they write. It decides which rules the DM sees standing beside
/// each other after a build folder and an installation have both listened once - and without the
/// distinction every development run would leave a rule behind pointing at a path that no longer
/// exists (Part 9).
/// <para>
/// The source file is LINKED into this project, exactly as it is into the three helpers. A project
/// reference would turn a helper into a library and give up the name in the elevation prompt, which
/// is the whole reason they are separate executables - and the architecture test forbids it.
/// </para>
/// </summary>
public sealed class FirewallRuleNameTests
{
    [Fact]
    public void A_copy_in_a_build_folder_is_marked_as_development()
    {
        var name = FirewallRule.NameFor(@"C:\Users\someone\source\DnDOverlay\src\DnDOverlay.Control\bin\Debug\net10.0-windows");

        Assert.Equal("DnDOverlay Control (dev)", name);
    }

    [Fact]
    public void A_copy_where_the_installer_puts_it_is_not()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "DnDOverlay",
            "Control");

        Assert.Equal("DnDOverlay Control", FirewallRule.NameFor(installed));
        Assert.True(FirewallRule.Installed(installed));
    }

    /// <summary>
    /// Windows writes its own rules in lower case - measured - and a path comparison that cared
    /// about case would call the installed copy a development one on a machine where nothing was
    /// wrong.
    /// </summary>
    [Fact]
    public void The_comparison_ignores_case()
    {
        var installed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "DnDOverlay");

        Assert.True(FirewallRule.Installed(installed.ToUpperInvariant()));
        Assert.True(FirewallRule.Installed(installed.ToLowerInvariant()));
    }

    /// <summary>
    /// The rule points at the control beside the helper, never at the helper itself - that is what
    /// makes "my own directory" a sufficient answer and keeps arguments out of an elevated program.
    /// </summary>
    [Fact]
    public void The_rule_points_at_the_control_beside_the_helper()
    {
        Assert.Equal(
            @"C:\somewhere\DnDOverlay.Control.exe",
            FirewallRule.PathIn(@"C:\somewhere"));
    }
}
