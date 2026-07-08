// Shared date-formatting helpers extracted from components that had identical copies.
// Behavior is preserved exactly; do not change formats without checking every caller.

// Short numeric date "dd/MM/yy", or 'N/A' when missing.
export function formatDate(date: Date | string | undefined): string {
  if (!date) return 'N/A';
  const d = new Date(date);
  const day = String(d.getDate()).padStart(2, '0');
  const month = String(d.getMonth() + 1).padStart(2, '0');
  const year = String(d.getFullYear()).slice(-2);
  return `${day}/${month}/${year}`;
}

// Full localized date + time in ro-RO, or 'N/A' when missing.
export function formatDateTime(date: Date | string | undefined): string {
  if (!date) return 'N/A';
  return new Date(date).toLocaleString('ro-RO');
}

// Coarse relative age: "Xd/Xh/Xm ago", "just now", or '' when missing.
export function getRelativeTime(date: Date | string | undefined): string {
  if (!date) return '';
  const now = new Date().getTime();
  const then = new Date(date).getTime();
  const diff = now - then;

  const seconds = Math.floor(diff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (days > 0) return `${days}d ago`;
  if (hours > 0) return `${hours}h ago`;
  if (minutes > 0) return `${minutes}m ago`;
  return 'just now';
}
