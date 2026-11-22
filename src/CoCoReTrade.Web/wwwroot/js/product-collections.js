(() => {
  document.querySelectorAll('[data-product-collection]').forEach(root => {
    const form = root.querySelector('[data-collection-form]');
    if (!form) return;
    const selectAll = form.querySelector('[data-collection-all]');
    const clear = form.querySelector('[data-collection-clear]');
    const remove = form.querySelector('[data-collection-remove]');
    const feedback = root.querySelector('[data-collection-feedback]');
    const isSaved = root.dataset.productCollection === 'saved';
    const grid = root.querySelector('.product-grid');
    const modeToggle = isSaved && grid ? document.createElement('button') : null;
    let selecting = false;
    let busy = false;
    const items = () => Array.from(root.querySelectorAll('[data-product-item]'));
    const choices = () => Array.from(root.querySelectorAll('[data-product-select]'));
    function refresh() {
      const cards = items();
      const selected = choices().filter(input => input.checked && !input.disabled);
      if (modeToggle) {
        selecting = cards.length > 0 && (selecting || selected.length > 0);
        modeToggle.textContent = selecting ? 'Xong' : 'Chọn sản phẩm';
        modeToggle.setAttribute('aria-expanded', String(selecting));
      }
      root.querySelector('[data-collection-count]').textContent = String(cards.length);
      root.querySelector('[data-selection-count]').textContent = `Đã chọn ${selected.length}`;
      root.querySelectorAll('[data-status-count]').forEach(counter => {
        counter.textContent = String(cards.filter(item => item.dataset.productStatus === counter.dataset.statusCount).length);
      });
      cards.forEach(item => item.querySelector('.product-card')?.classList.toggle('is-selected', Boolean(item.querySelector('[data-product-select]:checked'))));
      selectAll.disabled = busy || !choices().some(input => !input.disabled && !input.checked);
      clear.disabled = busy || selected.length === 0;
      remove.disabled = busy || selected.length === 0;
      form.hidden = cards.length === 0;
      if (grid) grid.hidden = cards.length === 0;
      const rule = root.querySelector('.collection-rule');
      if (rule) rule.hidden = cards.length === 0;
      root.querySelector('[data-collection-empty]').hidden = cards.length !== 0;
    }
    function showFeedback(message, error = false) {
      feedback.textContent = message;
      feedback.classList.toggle('is-error', error);
      feedback.hidden = false;
    }
    function removeItems(ids) {
      const removed = new Set(ids.map(id => String(id).toLowerCase()));
      items().forEach(item => { if (removed.has(item.dataset.productItem.toLowerCase())) item.remove(); });
      refresh();
    }
    if (modeToggle) {
      grid.id = 'saved-product-grid';
      modeToggle.type = 'button';
      modeToggle.className = 'btn btn-outline-wine collection-mode-toggle';
      modeToggle.setAttribute('aria-controls', grid.id);
      form.prepend(modeToggle);
      modeToggle.addEventListener('click', () => {
        selecting = !selecting;
        if (!selecting) choices().forEach(input => { input.checked = false; });
        refresh();
      });
    }
    root.addEventListener('change', event => { if (event.target.matches('[data-product-select]')) refresh(); });
    selectAll.addEventListener('click', () => { choices().filter(input => !input.disabled).forEach(input => { input.checked = true; }); refresh(); });
    clear.addEventListener('click', () => { choices().forEach(input => { input.checked = false; }); refresh(); });
    if (isSaved) document.addEventListener('coco:favorite-changed', event => {
      if (event.detail.selected === false) {
        removeItems([event.detail.productId]);
        if (!busy) showFeedback('Đã xóa món đồ khỏi yêu thích.');
      }
    });
    form.addEventListener('submit', async event => {
      event.preventDefault();
      if (busy) return;
      const selected = choices().filter(input => input.checked && !input.disabled);
      if (!selected.length) return;
      if (root.querySelector('form[data-favorite] button:disabled')) {
        showFeedback('Đang cập nhật yêu thích. Vui lòng đợi rồi thử lại.', true);
        return;
      }
      if (!window.confirm(`Xóa ${selected.length} món ${isSaved ? 'khỏi yêu thích' : 'khỏi tủ đồ'}?${isSaved ? '' : ' Chỉ món chưa kiểm định chính thức và chưa đăng bán được xóa.'}`)) return;
      const data = new FormData(form);
      busy = true;
      root.setAttribute('aria-busy', 'true');
      const controls = Array.from(root.querySelectorAll('button,input[type="checkbox"]')).map(control => ({ control, disabled: control.disabled }));
      controls.forEach(({ control }) => { control.disabled = true; });
      try {
        const response = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin', headers: { Accept: 'application/json' } });
        if (!response.headers.get('content-type')?.includes('application/json')) throw new Error('Phiên làm việc đã thay đổi. Hãy tải lại trang rồi thử lại.');
        const result = await response.json();
        if (!response.ok || !Array.isArray(result.removedIds)) throw new Error(result.error || 'Chưa xóa được món đồ. Vui lòng thử lại.');
        removeItems(result.removedIds);
        if (isSaved) result.removedIds.forEach(productId => document.dispatchEvent(new CustomEvent('coco:favorite-changed', { detail: { productId, selected: false } })));
        showFeedback(result.message || 'Đã cập nhật danh sách.');
      } catch (error) {
        showFeedback(error instanceof TypeError ? 'Không kết nối được. Danh sách vẫn được giữ; hãy thử lại.' : error.message, true);
      } finally {
        busy = false;
        root.removeAttribute('aria-busy');
        controls.forEach(({ control, disabled }) => { if (control.isConnected) control.disabled = disabled; });
        refresh();
      }
    });
    refresh();
  });
})();
