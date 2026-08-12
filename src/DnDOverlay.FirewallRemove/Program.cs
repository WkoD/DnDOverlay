using DnDOverlay.Firewall;

// Removes EVERY rule of our name, not one - that is the point of having this at all. Duplicates
// are what a repeated "add" used to leave behind, and what the control's reachability view
// reports when it finds more than one (Part 7, Part 9).
var removed = FirewallRule.Delete();

FirewallRule.Report("Removed", removed);

return removed;
