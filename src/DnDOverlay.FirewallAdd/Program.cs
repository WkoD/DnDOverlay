using DnDOverlay.Firewall;

// Delete first, then add - see FirewallRule.Add for why "set rule" is the wrong shape, and
// FirewallRule.Delete for why it goes by path. The deletion is allowed to fail: on a fresh machine
// there is nothing to remove, and netsh answers that with a non-zero code. Swallowing it here is
// what keeps the first run from failing.
FirewallRule.Delete();

// Home and domain. The public variant is a separate executable, because that is the difference the
// UAC prompt has to be able to name (Part 9).
var added = FirewallRule.Add(FirewallRule.HomeProfiles);

FirewallRule.Report("Added", added);

// The exit code is the whole answer to the caller. It reads no output: netsh speaks the language
// of the machine it runs on, and a control that parsed German sentences on a German Windows would
// be a control that works in one country.
return added;
