/**
 * Retreives the Windows/NTLM credentials that we must use when fetching resources
 * from an external workspace provider.
 *
 * This function requires a decryption key. The key is the same key that was used
 * to encrypt the tokens when they were stored in localStorage with the
 * `setExternalWorkspaceTokens` function.
 */
export async function getExternalWorkspaceTokens(
  decryptionKey: string
): Promise<{ endpoint: string; username: string; password: string; name: string }[] | null> {
  const encryptedTokens = localStorage.getItem('externalWorkspaceTokens');
  if (!encryptedTokens) {
    return [];
  }

  if (!Uint8Array.fromBase64) {
    throw new Error(
      'Uint8Array.fromBase64 is not defined. Please ensure that the polyfill for Uint8Array.fromBase64 is included.'
    );
  }

  const hashedInputKey = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(decryptionKey)); // AES-GCM requires 128 or 256 bit keys
  const key = await crypto.subtle.importKey('raw', hashedInputKey, { name: 'AES-GCM' }, false, ['decrypt']);

  const combined = Uint8Array.fromBase64!(encryptedTokens, {
    alphabet: 'base64',
    lastChunkHandling: 'loose',
  });

  // the IV (12 bytes for AES-GCM) is stored ahead of the ciphertext
  const iv = combined.slice(0, 12);
  const ciphertext = combined.slice(12);

  let decryptedTokens: ArrayBuffer;
  try {
    decryptedTokens = await crypto.subtle.decrypt(
      {
        name: 'AES-GCM',
        iv,
      },
      key,
      ciphertext
    );
  } catch (error) {
    throw new Error('externalWorkspaces.decryptFailed');
  }

  const decodedTokensJson = new TextDecoder().decode(decryptedTokens);
  const decodedTokens = JSON.parse(decodedTokensJson);

  if (!Array.isArray(decodedTokens)) {
    throw new Error('Decrypted tokens are not in the expected array format.');
  }

  if (
    !decodedTokens.every(
      (token): token is { endpoint: string; username: string; password: string; name?: string } =>
        typeof token.endpoint === 'string' && typeof token.username === 'string' && typeof token.password === 'string'
    )
  ) {
    throw new Error('Decrypted tokens do not have the expected structure.');
  }

  return decodedTokens.map((token) => ({ ...token, name: token.name ?? '' }));
}
