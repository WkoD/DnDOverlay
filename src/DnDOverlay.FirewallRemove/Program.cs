using DnDOverlay.Firewall;

// Removes EVERY inbound rule for this program path - ours, the ones Windows wrote when the "allow
// access?" box was answered or dismissed, and anything somebody built by hand. That breadth is the
// point rather than a side effect: a block rule left behind by a dismissed box beats every allow
// beside it, and a removal that went by name would walk straight past it (Part 9).
//
// The caller says so before it elevates. "Remove" means remove.
var removed = FirewallRule.Delete();

FirewallRule.Report("Removed", removed);

return removed;
