(() => {
  const page = document.querySelector('.inspection-review-page');
  if (!page) return;
  const gallery = page.querySelector('[data-inspection-gallery]');
  const main = gallery?.querySelector('[data-inspection-main]');
  gallery?.querySelectorAll('[data-inspection-image]').forEach(button => {
    button.addEventListener('click', () => {
      main.src = button.dataset.inspectionImage;
      main.alt = button.dataset.imageAlt;
      gallery.querySelectorAll('[data-inspection-image]').forEach(item => {
        const active = item === button;
        item.classList.toggle('is-active', active);
        item.setAttribute('aria-pressed', String(active));
      });
    });
  });
  const review = page.querySelector('[data-preliminary-review]');
  if (!review) return;
  const buttons = Array.from(review.querySelectorAll('[data-preliminary-choice]'));
  const panels = Array.from(review.querySelectorAll('[data-preliminary-panel]'));
  function select(choice, focus = false) {
    buttons.forEach(button => {
      const active = button.dataset.preliminaryChoice === choice;
      button.classList.toggle('is-active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    panels.forEach(panel => {
      const active = panel.dataset.preliminaryPanel === choice;
      panel.hidden = !active;
      panel.querySelectorAll('textarea').forEach(input => { input.disabled = !active; });
      if (active && focus) panel.querySelector('textarea')?.focus();
    });
  }
  buttons.forEach(button => button.addEventListener('click', () => select(button.dataset.preliminaryChoice, true)));
  panels.forEach(form => form.addEventListener('submit', event => {
    const messages = {
      approve: 'Duyệt sơ bộ món đồ này? Món đồ sẽ chờ kiểm định thực tế tại CoCo.',
      update: 'Gửi yêu cầu cập nhật cùng lý do này cho người ký gửi?',
      reject: 'Từ chối ký gửi món đồ này? Người ký gửi sẽ nhận thông báo kèm lý do.'
    };
    if (!window.confirm(messages[form.dataset.preliminaryPanel])) event.preventDefault();
  }));
  select('approve');
})();
