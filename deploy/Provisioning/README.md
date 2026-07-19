# Provisioning (Lead-owned)

Placeholder for OS-hardening + kiosk provisioning assets (Blueprint §5):

- Shell Launcher V2 configuration (the kiosk App as the custom shell).
- Assigned Access / lockdown XML.
- SQLCipher passphrase sealing (DPAPI/TPM) bootstrap.
- Licensing key material handling — **private signing key never lives here or in git.**
- Tailscale tailnet enrolment + ACL policy (segment sync vs. remote-support tunnel).

None of these files are committed. See `.gitignore`.
