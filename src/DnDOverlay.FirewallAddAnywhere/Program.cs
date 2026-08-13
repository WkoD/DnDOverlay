using DnDOverlay.Firewall;

// Same shape as FirewallAdd, one constant different - and that constant is the whole reason this
// is a separate executable (see the project file).
FirewallRule.Delete();

// Private and domain stay in the set. Writing "public only" would be right for the moment the
// button is pressed and wrong afterwards: the rule would apply in every foreign network and stop
// applying the moment the machine came home - working where it is not wanted and not where it is
// (Part 9).
var added = FirewallRule.Add(FirewallRule.EveryProfile);

FirewallRule.Report("Added, public included", added);

return added;
