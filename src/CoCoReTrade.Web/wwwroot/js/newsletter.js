(() => {
  const form = document.querySelector('[data-newsletter-form]');
  if (!form) return;
  const email = form.querySelector('[name="Email"]');
  const button = form.querySelector('button[type="submit"]');
  const status = form.querySelector('[data-newsletter-status]');

  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (button.disabled) return;
    button.disabled = true;
    button.textContent = 'Đang gửi';
    form.setAttribute('aria-busy', 'true');
    status.hidden = true;
    email.removeAttribute('aria-invalid');

    try {
      const response = await fetch(form.action, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { Accept: 'application/json' },
        body: new FormData(form)
      });
      if (!response.headers.get('content-type')?.includes('application/json')) {
        throw new Error(response.status === 429
          ? 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau ít phút.'
          : 'Chưa thể đăng ký. Vui lòng tải lại trang rồi thử lại.');
      }
      const result = await response.json();
      if (!response.ok || !result.success) {
        if (response.status === 400) email.setAttribute('aria-invalid', 'true');
        throw new Error(result.message || 'Chưa thể đăng ký. Vui lòng thử lại.');
      }
      email.value = '';
      status.textContent = result.message;
      status.classList.remove('is-error');
    } catch (error) {
      status.textContent = error instanceof TypeError
        ? 'Kết nối bị gián đoạn. Vui lòng thử đăng ký lại.'
        : error.message;
      status.classList.add('is-error');
    } finally {
      status.hidden = false;
      button.disabled = false;
      button.textContent = 'Đăng ký';
      form.removeAttribute('aria-busy');
    }
  });
})();
