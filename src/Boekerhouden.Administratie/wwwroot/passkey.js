// Passkeys (WebAuthn): de opties bij de server ophalen, de browser de passkey laten maken of gebruiken, en het
// resultaat met het formulier naar de server sturen. Een knop met data-passkey="nieuw" of "inloggen" start dit.
(() => {
    const ondersteund = () => window.PublicKeyCredential && PublicKeyCredential.parseCreationOptionsFromJSON;

    async function opties(url, formulier) {
        const token = formulier.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
        const antwoord = await fetch(url, { method: 'POST', headers: { RequestVerificationToken: token }, credentials: 'same-origin' });
        if (!antwoord.ok) {
            throw new Error('De server gaf geen passkey-opties.');
        }

        return await antwoord.json();
    }

    document.addEventListener('click', async gebeurtenis => {
        const knop = gebeurtenis.target.closest('[data-passkey]');
        if (!knop) {
            return;
        }

        gebeurtenis.preventDefault();
        const formulier = knop.closest('form');
        const melding = formulier.querySelector('[data-passkey-melding]');
        if (!ondersteund()) {
            melding.textContent = 'Deze browser ondersteunt geen passkeys.';
            return;
        }

        try {
            let sleutel;
            if (knop.dataset.passkey === 'nieuw') {
                const json = await opties('/account/passkey/opties-nieuw', formulier);
                sleutel = await navigator.credentials.create({ publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(json) });
            } else {
                const json = await opties('/account/passkey/opties-inloggen', formulier);
                sleutel = await navigator.credentials.get({ publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(json) });
            }

            formulier.querySelector('input[name$="CredentialJson"]').value = JSON.stringify(sleutel);
            formulier.submit();
        } catch (fout) {
            melding.textContent = fout.name === 'NotAllowedError' ? 'Geannuleerd of niet toegestaan.' : 'Dat lukte niet: ' + fout.message;
        }
    });
})();
