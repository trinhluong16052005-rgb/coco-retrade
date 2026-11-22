(() => {
  'use strict';
  const root = document.querySelector('[data-wallet-banking]');
  if (!root) return;
  const form = root.querySelector('[data-withdrawal-form]');
  const picker = root.querySelector('[data-bank-picker]');
  const list = root.querySelector('[data-bank-list]');
  const editor = root.querySelector('[data-bank-editor]');
  const editorForm = editor.querySelector('form');
  const prompt = root.querySelector('[data-save-prompt]');
  const fields = root.querySelector('[data-withdrawal-bank-fields]');
  let accounts = JSON.parse(root.querySelector('#wallet-bank-data').textContent);
  let editing = null;
  let decisionMade = false;
  let busy = false;
  const endpoint = '/api/v1/wallet/bank-accounts';
  const feedback = root.querySelector('[data-wallet-feedback]');
  const editorFeedback = root.querySelector('[data-editor-feedback]');
  const input = name => form.elements.namedItem(name);
  const editorInput = name => editorForm.elements.namedItem(name);
  const show = (element, message) => { element.textContent = message; element.hidden = !message; };
  const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
  async function api(url, method = 'GET', body) {
    const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-CSRF-TOKEN': token },
      body: body === undefined ? undefined : JSON.stringify(body) });
    const data = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new Error(data?.error || (response.status === 401 ? 'Phiên đăng nhập đã hết. Hãy đăng nhập lại.' : 'Chưa lưu được thay đổi. Hãy thử lại.'));
    return data;
  }
  function selectedId() { return input('SavedAccountId').value; }
  function summary(bank, number, holder) {
    const target = root.querySelector('[data-selected-bank]');
    const title = document.createElement('strong');
    title.textContent = bank && number ? `${bank} · ${number}` : 'Chọn hoặc thêm tài khoản ngân hàng';
    target.replaceChildren(title);
    if (holder) { const owner = document.createElement('small'); owner.textContent = holder; target.append(owner); }
  }
  function select(account) {
    input('SavedAccountId').value = account?.id || '';
    input('SavedAccountVersion').value = account?.rowVersion || '';
    input('BankName').value = account?.bankName || '';
    input('BankAccountNumber').value = account?.accountNumber || '';
    input('AccountHolder').value = account?.accountHolder || '';
    input('SaveAccount').value = 'false';
    fields.hidden = !!account?.id;
    summary(account?.bankName, account?.accountNumber, account?.accountHolder);
    decisionMade = false;
    picker.open = false;
    render();
  }
  function render() {
    list.replaceChildren();
    if (!accounts.length) { const empty = document.createElement('p'); empty.className = 'wallet-note px-3'; empty.textContent = 'Chưa có tài khoản đã lưu.'; list.append(empty); }
    for (const account of accounts) {
      const row = document.createElement('div'); row.className = 'wallet-bank-option';
      row.classList.toggle('is-selected', account.id === selectedId());
      const title = document.createElement('strong'); title.textContent = account.bankName;
      if (account.isDefault) { const badge = document.createElement('span'); badge.className = 'wallet-bank-default'; badge.textContent = 'Mặc định'; title.append(badge); }
      const number = document.createElement('p'); number.textContent = account.accountNumber;
      const owner = document.createElement('p'); owner.textContent = account.accountHolder;
      const actions = document.createElement('div'); actions.className = 'wallet-bank-option-actions';
      for (const [action, label] of [['select', account.id === selectedId() ? 'Đang chọn' : 'Chọn'], ['edit', 'Chỉnh sửa'], ['delete', 'Xóa'], ...(!account.isDefault ? [['default', 'Đặt mặc định']] : [])]) {
        const button = document.createElement('button'); button.type = 'button'; button.dataset.action = action; button.dataset.id = account.id; button.textContent = label;
        button.setAttribute('aria-label', `${label} ${account.bankName} ${account.accountNumber}`);
        button.disabled = busy; actions.append(button);
      }
      row.append(title, number, owner, actions); list.append(row);
    }
  }
  async function refresh() {
    const id = selectedId(); accounts = await api(endpoint);
    const account = accounts.find(a => a.id === id);
    if (id) select(account || accounts.find(a => a.isDefault) || accounts[0]);
    else render();
  }
  function openEditor(account) {
    editing = account || null; editorForm.reset(); show(editorFeedback, '');
    editor.querySelector('h2').textContent = account ? 'Chỉnh sửa tài khoản ngân hàng' : 'Thêm tài khoản ngân hàng';
    editorInput('BankName').value = account?.bankName || '';
    editorInput('AccountNumber').value = account?.accountNumber || '';
    editorInput('AccountHolder').value = account?.accountHolder || '';
    editorInput('IsDefault').checked = !!account?.isDefault;
    editor.querySelector('[data-bank-use]').hidden = !!account;
    show(editor.querySelector('[data-qr-feedback]'), 'Ảnh được đọc trên thiết bị. Kiểm tra lại thông tin trước khi lưu.');
    picker.open = false; editor.showModal();
  }
  root.querySelector('[data-bank-add]').addEventListener('click', () => openEditor());
  root.querySelector('[data-dialog-close]').addEventListener('click', () => editor.close());
  document.addEventListener('click', event => { if (!picker.contains(event.target)) picker.open = false; });
  picker.addEventListener('keydown', event => { if (event.key === 'Escape') { picker.open = false; picker.querySelector('summary').focus(); } });
  list.addEventListener('click', async event => {
    const button = event.target.closest('[data-action]'); if (!button || busy) return;
    const account = accounts.find(a => a.id === button.dataset.id); if (!account) return;
    if (button.dataset.action === 'select') return select(account);
    if (button.dataset.action === 'edit') return openEditor(account);
    if (button.dataset.action === 'delete' && !window.confirm('Xóa tài khoản ngân hàng đã lưu? Lịch sử rút tiền vẫn được giữ nguyên.')) return;
    busy = true; render(); show(feedback, '');
    try {
      await api(`${endpoint}/${account.id}${button.dataset.action === 'default' ? '/default' : ''}`, button.dataset.action === 'delete' ? 'DELETE' : 'POST', { rowVersion: account.rowVersion });
      await refresh();
    } catch (error) { show(feedback, error.message); }
    finally { busy = false; render(); }
  });
  const editorValues = () => ({ id: editing?.id || null, rowVersion: editing?.rowVersion || null,
    bankName: editorInput('BankName').value.trim(), accountNumber: editorInput('AccountNumber').value.trim(),
    accountHolder: editorInput('AccountHolder').value.trim(), isDefault: editorInput('IsDefault').checked });
  root.querySelector('[data-bank-use]').addEventListener('click', () => {
    if (!editorForm.reportValidity()) return;
    select(editorValues()); editor.close();
  });
  editorForm.addEventListener('submit', async event => {
    event.preventDefault(); if (busy) return;
    busy = true; show(editorFeedback, '');
    const submit = editorForm.querySelector('[type="submit"]'); submit.disabled = true;
    try {
      const account = await api(endpoint, 'POST', editorValues());
      accounts = accounts.filter(a => a.id !== account.id).map(a => account.isDefault ? { ...a, isDefault: false } : a);
      accounts.push(account); select(account); editor.close(); await refresh();
    } catch (error) { show(editor.open ? editorFeedback : feedback, error.message); }
    finally { busy = false; submit.disabled = false; render(); }
  });
  fields.addEventListener('input', () => { input('SavedAccountId').value = ''; input('SavedAccountVersion').value = ''; decisionMade = false; summary(input('BankName').value, input('BankAccountNumber').value, input('AccountHolder').value); });
  form.addEventListener('submit', event => {
    if (!selectedId() && !decisionMade) { event.preventDefault(); prompt.showModal(); }
    else if (busy) event.preventDefault();
    else { busy = true; form.querySelector('[type="submit"]').disabled = true; }
  });
  prompt.querySelectorAll('[data-save-choice]').forEach(button => button.addEventListener('click', () => {
    input('SaveAccount').value = button.dataset.saveChoice; decisionMade = true; prompt.close(); form.requestSubmit();
  }));
  let banksPromise;
  const banks = () => banksPromise ||= fetch('/data/vietqr-banks.json', { cache: 'force-cache' }).then(r => {
    if (!r.ok) throw new Error('Chưa tải được danh sách ngân hàng. Hãy nhập thủ công.'); return r.json();
  });
  banks().then(items => {
    const names = root.querySelector('#wallet-bank-names');
    for (const bank of items) { const option = document.createElement('option'); option.value = bank.shortName; option.label = bank.name; names.append(option); }
  }).catch(() => {});
  root.querySelector('[data-bank-qr]').addEventListener('change', async event => {
    const file = event.target.files[0]; if (!file) return;
    const status = editor.querySelector('[data-qr-feedback]');
    try {
      if (file.size > 8_000_000 || !['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) throw new Error('Chọn ảnh PNG, JPG hoặc WebP nhỏ hơn 8 MB.');
      show(status, 'Đang đọc QR…');
      const bitmap = await createImageBitmap(file);
      try {
        const ratio = Math.min(1, 1800 / Math.max(bitmap.width, bitmap.height));
        const canvas = document.createElement('canvas'); canvas.width = Math.max(1, Math.round(bitmap.width * ratio)); canvas.height = Math.max(1, Math.round(bitmap.height * ratio));
        const context = canvas.getContext('2d', { willReadFrequently: true }); context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
        const image = context.getImageData(0, 0, canvas.width, canvas.height);
        const code = window.jsQR(image.data, image.width, image.height, { inversionAttempts: 'attemptBoth' });
        if (!code) throw new Error('Không tìm thấy QR. Chọn ảnh rõ nét hơn hoặc nhập thủ công.');
        const result = await api(`${endpoint}/read-qr`, 'POST', { payload: code.data });
        const bank = (await banks()).find(b => b.bin === result.bankBin);
        if (!bank) throw new Error('Ngân hàng chưa có trong danh sách nhận diện. Hãy nhập thông tin thủ công.');
        editorInput('BankName').value = bank.shortName;
        editorInput('AccountNumber').value = result.accountNumber;
        editorInput('AccountHolder').value = result.accountHolder || '';
        show(status, result.accountHolder ? 'Đã nhận diện. Kiểm tra lại thông tin trước khi lưu.' : 'Đã nhận diện ngân hàng và số tài khoản. Nhập thêm tên chủ tài khoản.');
      } finally { bitmap.close(); }
    } catch (error) { show(status, error.message); }
  });
  render();
})();
