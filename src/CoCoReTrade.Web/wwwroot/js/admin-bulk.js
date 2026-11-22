(() => {
  'use strict';
  document.querySelectorAll('form[data-admin-bulk]').forEach(form => {
    const items = Array.from(document.querySelectorAll('input[data-bulk-item]')).filter(input => input.form === form && !input.disabled);
    const all = Array.from(document.querySelectorAll('[data-bulk-all]')).filter(input => input.dataset.bulkTarget === form.id);
    const submit = form.querySelector('[data-bulk-submit]');
    const count = form.querySelector('[data-bulk-count]');
    const noun = form.dataset.bulkNoun || 'mục';
    const update = () => {
      const selected = items.filter(input => input.checked).length;
      all.forEach(input => { input.checked = items.length > 0 && selected === items.length; input.indeterminate = selected > 0 && selected < items.length; input.disabled = items.length === 0; });
      if (submit) submit.disabled = selected === 0;
      if (count) count.textContent = `Đã chọn ${selected} ${noun}`;
    };
    all.forEach(input => input.addEventListener('change', () => { items.forEach(item => { item.checked = input.checked; }); update(); }));
    items.forEach(input => input.addEventListener('change', update));
    form.addEventListener('submit', event => {
      const selected = items.filter(input => input.checked).length;
      if (selected === 0 || !window.confirm(`Xóa ${selected} ${noun} đã chọn? ${form.dataset.bulkConfirm || ''}`)) event.preventDefault();
      else { if (submit) submit.disabled = true; }
    });
    update();
  });
  document.querySelectorAll('form[data-admin-single-delete]').forEach(form => {
    form.addEventListener('submit', event => {
      if (!window.confirm(`Xóa ${form.dataset.deleteKind || 'mục'} “${form.dataset.deleteName || ''}”? ${form.dataset.deleteNote || ''}`)) event.preventDefault();
    });
  });
})();
