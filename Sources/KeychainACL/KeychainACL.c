// SecAccessCreate is deprecated, but it is the public API that sets a legacy keychain item's access list.
// The data-protection keychain ignores this list (and an ad-hoc CLI cannot use it), so the login keychain is used.
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
#include "KeychainACL.h"

OSStatus ambysto_add_untrusted_generic_password(CFStringRef service, CFStringRef account, CFDataRef secret) {
    SecAccessRef access = NULL;
    CFArrayRef none = CFArrayCreate(kCFAllocatorDefault, NULL, 0, &kCFTypeArrayCallBacks);
    OSStatus status = SecAccessCreate(CFSTR("Ambysto license signing key"), none, &access);
    CFRelease(none);
    if (status != errSecSuccess) return status;
    const void *keys[] = { kSecClass, kSecAttrService, kSecAttrAccount, kSecAttrLabel, kSecValueData, kSecAttrAccess };
    const void *values[] = { kSecClassGenericPassword, service, account, CFSTR("Ambysto license signing key"), secret, access };
    CFDictionaryRef query = CFDictionaryCreate(kCFAllocatorDefault, keys, values, 6,
                                               &kCFTypeDictionaryKeyCallBacks, &kCFTypeDictionaryValueCallBacks);
    status = SecItemAdd(query, NULL);
    CFRelease(query);
    CFRelease(access);
    return status;
}
