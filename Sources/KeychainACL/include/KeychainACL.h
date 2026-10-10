#ifndef KEYCHAIN_ACL_H
#define KEYCHAIN_ACL_H
#include <CoreFoundation/CoreFoundation.h>
#include <Security/Security.h>

/// Adds a generic password to the file-based login keychain whose access list trusts no application,
/// so macOS asks for the login password on every read. Returns an OSStatus.
OSStatus ambysto_add_untrusted_generic_password(CFStringRef service, CFStringRef account, CFDataRef secret);

#endif
