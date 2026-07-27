# Security Policy

## Reporting a vulnerability

Please report suspected security or privacy vulnerabilities privately to the repository maintainers rather than opening a public issue. Include a clear reproduction path, affected version, impact, and any suggested mitigation.

Do not include real passwords, tokens, clipboard contents, or other secrets in a report.

## Security model

TypeyTypey intentionally keeps clipboard history in memory only and does not perform network communication. It cannot bypass Windows integrity boundaries or secure desktop protections; those platform restrictions are expected safeguards.

## Command pipe

Command-line options are relayed to the running instance over a local named pipe. Because one of
those commands types the current clipboard contents, access to that pipe is security relevant.

From v1.0.4 the pipe is created with an explicit DACL granting full control only to the user who
owns the running instance and to LocalSystem.

Through v1.0.3 the pipe used the Windows default security descriptor, measured as
`D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BA)(A;;FR;;;WD)(A;;FR;;;AN)` — full control for LocalSystem and
Administrators, and **read** access for Everyone and ANONYMOUS LOGON. Sending a command requires
write access to an inbound pipe, so that default did not allow another local user to trigger typing.
It did allow any local process to open the pipe and occupy its single instance, and extended reach
to anonymous logons with no purpose. The explicit DACL removes both.

This does not defend against a process already running as the same user, which can interact with the
application by design.

**Known limitation, unverified.** If a standard user elevates TypeyTypey by entering a *different*
administrator account's credentials at the UAC prompt, the elevated instance owns the pipe under
that administrator's account, and command-line options run by the original user may no longer reach
it. This has been reasoned from the access model rather than reproduced, and it appears to predate
the v1.0.4 change: the previous Windows default also withheld write access from other users.
