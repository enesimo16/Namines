export const DESK_URL = process.env.NEXT_PUBLIC_DESK_URL ?? 'http://localhost:3200';

/**
 * Desk'in `/handoff` ucuna gizli bir form POST'u ile gider — jeton bir
 * <a href> ya da window.open URL'inde DEĞİL, bu formun gövdesinde taşınır.
 * Bu, jetonun tarayıcı geçmişine ve `Referer` başlığına düşmesini engeller.
 */
export function openDeskHandoff(fields: Record<string, string>) {
  const form = document.createElement('form');
  form.method = 'POST';
  form.action = `${DESK_URL}/handoff`;
  form.target = '_blank';
  form.style.display = 'none';

  for (const [name, value] of Object.entries(fields)) {
    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = name;
    input.value = value;
    form.appendChild(input);
  }

  document.body.appendChild(form);
  form.submit();
  document.body.removeChild(form);
}
