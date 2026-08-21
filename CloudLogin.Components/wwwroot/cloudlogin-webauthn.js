// WebAuthn bridge for the CloudLogin account page.
//
// The browser's credential APIs exchange ArrayBuffers, while the server exchanges the
// Base64Url-encoded JSON that Fido2NetLib produces and consumes. This module does that
// translation in both directions and nothing else — no decisions about which credentials
// are acceptable are made here, since only the server's verification is trustworthy.

window.cloudLoginWebAuthn = (() => {

    const base64UrlToBuffer = (value) => {
        const padded = value.replace(/-/g, '+').replace(/_/g, '/');
        const binary = atob(padded.padEnd(padded.length + (4 - padded.length % 4) % 4, '='));
        const bytes = new Uint8Array(binary.length);

        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

        return bytes.buffer;
    };

    const bufferToBase64Url = (buffer) => {
        const bytes = new Uint8Array(buffer);
        let binary = '';

        for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);

        return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    };

    return {
        /** True when this browser/context can do WebAuthn at all (needs a secure context). */
        isSupported: () => !!(window.PublicKeyCredential && navigator.credentials),

        /** True when the device has a built-in authenticator (Touch ID, Windows Hello, Android biometrics). */
        hasPlatformAuthenticator: async () => {
            if (!window.PublicKeyCredential?.isUserVerifyingPlatformAuthenticatorAvailable) return false;

            try {
                return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            } catch {
                return false;
            }
        },

        /**
         * Runs navigator.credentials.create() against server-issued options and returns the
         * attestation as JSON for the server to verify.
         */
        createCredential: async (optionsJson) => {
            const options = JSON.parse(optionsJson);

            options.challenge = base64UrlToBuffer(options.challenge);
            options.user.id = base64UrlToBuffer(options.user.id);

            if (options.excludeCredentials) {
                options.excludeCredentials = options.excludeCredentials.map(c => ({
                    ...c,
                    id: base64UrlToBuffer(c.id)
                }));
            }

            const credential = await navigator.credentials.create({ publicKey: options });

            if (!credential) throw new Error('No credential was created.');

            return JSON.stringify({
                id: credential.id,
                rawId: bufferToBase64Url(credential.rawId),
                type: credential.type,
                extensions: credential.getClientExtensionResults(),
                response: {
                    attestationObject: bufferToBase64Url(credential.response.attestationObject),
                    // Fido2NetLib's wire contract spells this clientDataJSON (all-caps JSON,
                    // matching the browser's own PublicKeyCredential property name) — the
                    // server's deserializer is case-sensitive, so this exact casing matters.
                    clientDataJSON: bufferToBase64Url(credential.response.clientDataJSON),
                    transports: credential.response.getTransports ? credential.response.getTransports() : []
                }
            });
        },

        /**
         * Runs navigator.credentials.get() against server-issued options and returns the
         * assertion as JSON for the server to verify.
         */
        getAssertion: async (optionsJson) => {
            const options = JSON.parse(optionsJson);

            options.challenge = base64UrlToBuffer(options.challenge);

            if (options.allowCredentials) {
                options.allowCredentials = options.allowCredentials.map(c => ({
                    ...c,
                    id: base64UrlToBuffer(c.id)
                }));
            }

            const assertion = await navigator.credentials.get({ publicKey: options });

            if (!assertion) throw new Error('No assertion was produced.');

            return JSON.stringify({
                id: assertion.id,
                rawId: bufferToBase64Url(assertion.rawId),
                type: assertion.type,
                extensions: assertion.getClientExtensionResults(),
                response: {
                    authenticatorData: bufferToBase64Url(assertion.response.authenticatorData),
                    // See the matching note in createCredential() — casing must match exactly.
                    clientDataJSON: bufferToBase64Url(assertion.response.clientDataJSON),
                    signature: bufferToBase64Url(assertion.response.signature),
                    userHandle: assertion.response.userHandle ? bufferToBase64Url(assertion.response.userHandle) : null
                }
            });
        },

        /** Opens a coordinate pair on an external map, in a new tab. */
        openMap: (latitude, longitude) => {
            window.open(
                `https://www.google.com/maps/search/?api=1&query=${latitude},${longitude}`,
                '_blank',
                'noopener,noreferrer');
        }
    };
})();
