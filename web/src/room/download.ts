/** Saves a blob through a temporary object URL and anchor click. */
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url; link.download = filename; link.click();
  // Revoke after the browser has started reading the object URL.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
