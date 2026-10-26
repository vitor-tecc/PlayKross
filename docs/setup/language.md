# Interface language

The Windows app and room website default to Brazilian Portuguese (`pt-BR`). Use the small selector in the upper-right corner to switch between **Português (BR)** and **English**. Switching updates visible text without restarting a broadcast or reloading the website.

The app saves the preference under the current Windows user's local application data (`PlayKross/language.txt`). The website saves it in browser local storage for that website; the in-app browser has its own browser profile. If storage is unavailable, the selection still works for the current session. A fresh profile starts in Brazilian Portuguese regardless of the operating system or browser language.

Interface labels, capture/audio guidance, application-controlled errors and displayed diagnostics use the shared catalog at `web/src/i18n/pt-BR.json`, also embedded in the Windows app. App names, nicknames, device/window names, codec and network identifiers, and exported diagnostic data retain their original values. Browser/Windows permission dialogs and low-level messages supplied by external components follow those components' language settings.

Existing English-copy behavior checks explicitly select English in their test setup. The release verification gate remains in place; localization does not replace historical streaming/audio acceptance evidence.
