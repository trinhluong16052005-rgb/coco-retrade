(() => {
  'use strict';
  const root = document.querySelector('.coco-checkout');
  const form = root?.querySelector('[data-checkout-form]');
  if (!form) return;
  const byId = id => document.getElementById(id);
  const fromCart = root.dataset.fromCart === 'true';
  const type = byId('Fulfillment');
  const province = byId('Province'), ward = byId('Ward'), street = byId('StreetAddress');
  const submit = byId('checkoutSubmit'), error = byId('quoteError');
  const voucherInput = byId('VoucherCode'), voucherStatus = byId('voucherStatus');
  const applyVoucher = byId('applyVoucher'), clearVoucher = byId('clearVoucher');
  const recommendationList = byId('checkoutRecommendations');
  const token = form.querySelector('[name="__RequestVerificationToken"]').value;
  const selected = () => [...form.querySelectorAll('[name="ProductIds"]')].filter(el => el.type !== 'checkbox' || el.checked).map(el => el.value);
  const money = amount => new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(amount) + ' VNĐ';
  let generation = 0, recommendationGeneration = 0, controller, quoteReady = false;
  let voucherSelection = form.dataset.voucherSelection || '';
  let pricedVoucher = form.dataset.appliedVoucher || '';
  let applyingVoucher = false;
  function syncVoucherControls() {
    const draft = voucherInput.value.trim().toUpperCase();
    for (const option of form.querySelectorAll('[data-voucher-choice]')) option.checked = option.value === draft;
    applyVoucher.disabled = applyingVoucher || !selected().length;
    clearVoucher.hidden = !voucherSelection;
    clearVoucher.disabled = applyVoucher.disabled;
  }
  function syncFields() {
    const delivery = type.value === 'Shipping';
    byId('shippingFields').hidden = !delivery;
    byId('pickupFields').hidden = delivery;
    byId('checkoutPayment').hidden = !delivery;
    for (const field of [province, ward, street, byId('DeliveryNote')]) field.disabled = !delivery;
    for (const field of [province, ward, street]) field.required = delivery;
    submit.textContent = 'Đặt hàng';
    const checkboxes = [...form.querySelectorAll('.checkout-item-select')];
    const selectAll = byId('checkoutSelectAll');
    if (selectAll) {
      selectAll.checked = checkboxes.length > 0 && checkboxes.every(el => el.checked);
      selectAll.indeterminate = checkboxes.some(el => el.checked) && !selectAll.checked;
    }
    byId('checkoutEmptySelection').hidden = selected().length > 0;
    syncVoucherControls();
  }
  function invalidate() { quoteReady = false; submit.disabled = true; generation++; controller?.abort(); }
  async function fetchQuote(code, apply, signal) {
    const body = new FormData(form);
    body.set('VoucherCode', code);
    body.set(apply ? 'applyVoucher' : 'reprice', 'true');
    const response = await fetch('/Checkout/Quote', { method: 'POST', credentials: 'same-origin',
      headers: { Accept: 'application/json' }, body, signal });
    if (!response.headers.get('content-type')?.includes('application/json'))
      throw new Error('Phiên đăng nhập đã hết hạn. Hãy đăng nhập lại.');
    const quote = await response.json();
    if (!response.ok) throw new Error(quote.error || 'Không cập nhật được tổng tiền.');
    return quote;
  }
  function showVoucherStatus(message, invalid = false) {
    voucherStatus.textContent = message;
    voucherStatus.classList.toggle('text-danger', invalid);
  }
  async function updateQuote(voucherError = '') {
    invalidate(); syncFields();
    if (applyingVoucher) return;
    const current = generation;
    error.textContent = '';
    if (!selected().length) {
      for (const id of ['merchandiseAmount', 'shippingFee', 'totalAmount']) byId(id).textContent = money(0);
      byId('selectedCount').textContent = '(0 món)'; byId('discountRow').hidden = true;
      pricedVoucher = '';
      showVoucherStatus('');
      return;
    }
    controller = new AbortController(); byId('totalAmount').textContent = 'Đang tính';
    try {
      const quote = await fetchQuote(voucherSelection, false, controller.signal);
      if (current !== generation) return;
      byId('merchandiseAmount').textContent = money(quote.merchandiseAmount);
      byId('selectedCount').textContent = `(${quote.items.length} món)`;
      byId('shippingFee').textContent = quote.shippingFeePending ? 'Chọn tỉnh / thành phố' : money(quote.shippingFee);
      byId('discountAmount').textContent = '−' + money(quote.discountAmount);
      byId('discountRow').hidden = quote.discountAmount <= 0;
      byId('totalAmount').textContent = quote.shippingFeePending ? '—' : money(quote.totalAmount);
      pricedVoucher = quote.voucherCode || '';
      const applied = pricedVoucher ? 'Đã áp dụng ' + pricedVoucher : '';
      const problem = voucherError || quote.voucherError;
      showVoucherStatus(problem ? [problem, applied].filter(Boolean).join(' ') : applied, Boolean(problem));
      quoteReady = !quote.shippingFeePending; submit.disabled = !quoteReady;
    } catch (failure) {
      if (failure.name === 'AbortError' || current !== generation) return;
      byId('totalAmount').textContent = '—'; byId('discountRow').hidden = true;
      error.textContent = failure.message || 'Chưa kết nối được. Vui lòng thử lại.';
    }
  }
  async function chooseVoucher(code) {
    if (applyingVoucher || !selected().length) return;
    applyingVoucher = true;
    invalidate(); syncVoucherControls();
    showVoucherStatus('Đang cập nhật mã giảm giá.');
    let problem = '';
    try {
      const quote = await fetchQuote(code, true);
      voucherSelection = quote.voucherCode || '';
      voucherInput.value = voucherSelection;
    } catch (failure) {
      problem = failure.message;
    } finally {
      applyingVoucher = false;
    }
    await updateQuote(problem);
  }
  form.addEventListener('change', event => {
    if (event.target.id === 'checkoutSelectAll') {
      form.querySelectorAll('.checkout-item-select').forEach(el => { el.checked = event.target.checked; }); updateQuote();
    } else if (event.target.matches('[data-voucher-choice]')) {
      voucherInput.value = event.target.value;
      syncVoucherControls();
    } else if (event.target.matches('.checkout-item-select') || event.target === type || event.target === province) updateQuote();
  });
  voucherInput.addEventListener('input', syncVoucherControls);
  voucherInput.addEventListener('keydown', event => {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    chooseVoucher(voucherInput.value.trim());
  });
  applyVoucher.addEventListener('click', () => chooseVoucher(voucherInput.value.trim()));
  clearVoucher.addEventListener('click', () => chooseVoucher(''));
  form.querySelectorAll('[data-voucher-terms]').forEach(button => {
    const dialog = byId(button.dataset.voucherTerms);
    button.addEventListener('click', () => dialog.showModal());
    dialog.querySelector('[data-voucher-terms-close]').addEventListener('click', () => dialog.close());
  });
  byId('savedAddress')?.addEventListener('change', event => {
    const address = event.target.selectedOptions[0]; if (!address.value) return;
    for (const [id, key] of [['RecipientName', 'recipient'], ['RecipientPhone', 'phone'], ['Province', 'province'], ['Ward', 'ward'], ['StreetAddress', 'street']]) byId(id).value = address.dataset[key] || '';
    updateQuote();
  });
  const updateCount = count => document.querySelectorAll('[data-cart-count]').forEach(el => { el.textContent = count; el.hidden = count === 0; });
  async function reloadRecommendations() {
    const current = ++recommendationGeneration;
    const query = new URLSearchParams();
    if (!fromCart) query.set('productId', byId('ProductId').value);
    try {
      const response = await fetch('/Checkout/Recommendations?' + query, { credentials: 'same-origin', cache: 'no-store' });
      if (!response.ok || response.redirected) throw new Error();
      const markup = await response.text();
      if (current !== recommendationGeneration) return;
      recommendationList.innerHTML = markup;
      recommendationList.dispatchEvent(new Event('coco:recommendations-updated'));
    } catch {
      if (current === recommendationGeneration) recommendationList.replaceChildren();
    }
  }
  async function reloadItems(ids) {
    const query = new URLSearchParams(); ids.forEach(id => query.append('selectedIds', id));
    const response = await fetch('/Cart/Lines?' + query, { credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok || response.redirected) throw new Error('Chưa cập nhật được giỏ hàng. Vui lòng tải lại trang.');
    const items = byId('checkoutItems');
    items.innerHTML = await response.text();
    byId('checkoutItemCount').textContent = items.querySelectorAll('[data-checkout-item]').length + ' sản phẩm';
    await Promise.all([updateQuote(), reloadRecommendations()]);
  }
  form.addEventListener('click', async event => {
    const button = event.target.closest('[data-checkout-remove]'); if (!button) return;
    event.preventDefault(); button.disabled = true; invalidate();
    const body = new FormData(); body.set('productId', button.dataset.checkoutRemove); body.set('__RequestVerificationToken', token);
    try {
      const response = await fetch('/Cart/Remove', { method: 'POST', credentials: 'same-origin', headers: { Accept: 'application/json' }, body });
      if (!response.ok || response.redirected) throw new Error('Không bỏ được sản phẩm. Hãy tải lại và thử lại.');
      const result = await response.json(); updateCount(result.count);
      if (result.count === 0) { window.location.reload(); return; }
      await reloadItems(selected().filter(id => id !== button.dataset.checkoutRemove));
    } catch (failure) { error.textContent = failure.message; button.disabled = false; }
  });
  root.addEventListener('submit', async event => {
    const addForm = event.target.closest('[data-checkout-add]');
    if (!addForm) return;
    event.preventDefault(); const button = addForm.querySelector('button'); button.disabled = true;
    const status = byId('crossSellStatus'); status.textContent = '';
    try {
      const response = await fetch(addForm.action, { method: 'POST', body: new FormData(addForm), credentials: 'same-origin', headers: { Accept: 'application/json' } });
      if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Hãy đăng nhập lại để thêm sản phẩm.');
      const result = await response.json(); if (!response.ok) throw new Error(result.error || 'Không thêm được sản phẩm.');
      updateCount(result.count);
      if (fromCart) await reloadItems([...new Set([...selected(), addForm.querySelector('[name="productId"]').value])]);
      else await reloadRecommendations();
      status.textContent = fromCart ? 'Đã thêm sản phẩm vào giỏ và cập nhật tổng tiền.' : 'Đã thêm sản phẩm vào giỏ. Bạn có thể mua cùng các món khác tại Giỏ hàng.';
    } catch (failure) { status.textContent = failure.message; button.disabled = false; }
  });
  form.addEventListener('submit', event => {
    if (event.submitter?.hasAttribute('data-checkout-remove')) return;
    if (!quoteReady || !selected().length) { event.preventDefault(); updateQuote(); return; }
    voucherInput.value = pricedVoucher;
    submit.disabled = true; submit.textContent = 'Đang đặt hàng';
  });
  updateQuote();
})();
