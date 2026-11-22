(() => {
    'use strict';
    const modalElement = document.getElementById('coco-auth-modal');
    if (!modalElement || !window.bootstrap?.Modal) return;

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const content = modalElement.querySelector('[data-auth-modal-content]');
    const status = modalElement.querySelector('[data-auth-status]');
    const page = document.querySelector('[data-auth-page]');
    const pageContent = page?.querySelector('[data-auth-page-content]');
    let loading = null;
    let previousFocus = null;
    let baseReturnUrl = '/';
    let submitting = false;

    const localUrl = (value, fallback = '/') => {
        try {
            const url = new URL(value, location.origin);
            return url.origin === location.origin ? url.pathname + url.search + url.hash : fallback;
        } catch { return fallback; }
    };
    const authUrl = value => {
        try {
            const url = new URL(value, location.origin);
            return url.origin === location.origin && /^\/Account\/(Login|Register)\/?$/i.test(url.pathname) ? url : null;
        } catch { return null; }
    };
    const setStatus = message => {
        status.textContent = message || '';
        status.hidden = !message;
    };
    const focusContent = () => {
        const errors = content.querySelector('.validation-summary-errors, .field-validation-error');
        if (errors) {
            errors.setAttribute('tabindex', '-1');
            errors.focus();
        } else content.querySelector('[data-auth-title]')?.focus();
    };
    const mountFragment = (html, expectedMode) => {
        const template = document.createElement('template');
        template.innerHTML = html;
        const pane = template.content.querySelector('[data-auth-pane]');
        if (!pane || pane.dataset.authPane !== expectedMode || template.content.querySelector('script')) return false;
        content.replaceChildren(template.content);
        const modalTitle = document.getElementById('coco-auth-modal-title');
        modalTitle.textContent = expectedMode === 'register' ? 'Đăng ký CoCo' : 'Đăng nhập CoCo';
        return true;
    };
    const openForm = async (inputUrl, trigger) => {
        const url = authUrl(inputUrl);
        if (!url) return;
        if (submitting) return;
        if (trigger && !modalElement.contains(trigger)) {
            previousFocus = trigger;
            baseReturnUrl = localUrl(url.searchParams.get('returnUrl') || location.pathname + location.search + location.hash);
        }
        if (!url.searchParams.has('returnUrl')) url.searchParams.set('returnUrl', baseReturnUrl);
        const expectedMode = /\/Register\/?$/i.test(url.pathname) ? 'register' : 'login';
        loading?.abort();
        loading = new AbortController();
        const controller = loading;
        setStatus('');
        content.setAttribute('aria-busy', 'true');
        modal.show();
        try {
            const response = await fetch(url, {
                credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
                headers: { 'X-CoCo-Auth-Modal': '1', 'Accept': 'text/html' }
            });
            if (controller !== loading) return;
            if (!response.ok || response.headers.get('X-CoCo-Auth-Fragment') !== expectedMode || !response.headers.get('content-type')?.includes('text/html')) throw new Error('fragment');
            if (!mountFragment(await response.text(), expectedMode)) throw new Error('fragment');
            if (modalElement.classList.contains('show')) focusContent();
        } catch (error) {
            if (error.name !== 'AbortError') {
                setStatus('Chưa mở được biểu mẫu. Bạn có thể thử lại hoặc mở trang tài khoản.');
                if (!content.querySelector('[data-auth-pane]')) {
                    const link = document.createElement('a');
                    link.href = localUrl(url.href);
                    link.className = 'btn btn-outline-wine w-100';
                    link.textContent = 'Mở trang tài khoản';
                    link.dataset.authFallback = 'true';
                    content.replaceChildren(link);
                }
            }
        } finally {
            if (controller === loading) content.removeAttribute('aria-busy');
        }
    };

    document.addEventListener('click', event => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        const target = event.target.closest('a[href]');
        if (!target || target.hasAttribute('download') || target.dataset.authFallback || (target.target && target.target !== '_self')) return;
        const url = authUrl(target.href);
        if (!url) return;
        event.preventDefault();
        void openForm(url.href, target);
    });

    document.addEventListener('click', event => {
        const toggle = event.target.closest('[data-auth-password-toggle]');
        if (!toggle) return;
        const input = toggle.parentElement.querySelector('input');
        if (!input) return;
        const visible = input.type === 'password';
        input.type = visible ? 'text' : 'password';
        toggle.textContent = visible ? 'Ẩn' : 'Hiện';
        toggle.setAttribute('aria-pressed', String(visible));
        toggle.setAttribute('aria-label', visible ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
    });

    modalElement.addEventListener('submit', async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-auth-form]')) return;
        event.preventDefault();
        if (submitting || !form.reportValidity()) return;
        const confirm = form.elements.namedItem('ConfirmPassword');
        const password = form.elements.namedItem('Password');
        if (confirm && password && confirm.value !== password.value) {
            confirm.setCustomValidity('Mật khẩu xác nhận không khớp.');
            confirm.reportValidity();
            return;
        }
        const url = authUrl(form.action);
        if (!url) return;
        const mode = /\/Register\/?$/i.test(url.pathname) ? 'register' : 'login';
        submitting = true;
        setStatus('');
        const submit = form.querySelector('[type="submit"]');
        const initialText = submit?.textContent;
        const payload = new FormData(form);
        if (submit) { submit.disabled = true; submit.textContent = mode === 'register' ? 'Đang tạo tài khoản' : 'Đang đăng nhập'; }
        content.setAttribute('aria-busy', 'true');
        try {
            const response = await fetch(url, {
                method: 'POST', body: payload, credentials: 'same-origin', cache: 'no-store',
                headers: { 'X-CoCo-Auth-Modal': '1', 'Accept': 'application/json, text/html' }
            });
            const type = response.headers.get('content-type') || '';
            if (response.ok && type.includes('application/json')) {
                const result = await response.json();
                if (typeof result.redirectUrl !== 'string' || !result.redirectUrl.startsWith('/') || result.redirectUrl.startsWith('//')) throw new Error('redirect');
                location.assign(localUrl(result.redirectUrl));
                return;
            }
            if (response.ok && type.includes('text/html') && response.headers.get('X-CoCo-Auth-Fragment') === mode) {
                if (!mountFragment(await response.text(), mode)) throw new Error('fragment');
                focusContent();
            } else if (response.status === 400) {
                setStatus('Phiên biểu mẫu đã hết hạn. Đóng cửa sổ rồi mở lại để tiếp tục.');
            } else throw new Error('submit');
        } catch {
            // Do not automatically retry a POST: registration may already have succeeded.
            setStatus(mode === 'register' ? 'Chưa xác nhận được kết quả. Hãy kiểm tra email hoặc dùng Gửi lại mã xác thực trước khi thử lại.' : 'Chưa xác nhận được đăng nhập. Hãy thử tải lại trang trước khi tiếp tục.');
        } finally {
            submitting = false;
            content.removeAttribute('aria-busy');
            if (submit?.isConnected) { submit.disabled = false; submit.textContent = initialText; }
        }
    });
    modalElement.addEventListener('input', event => {
        if (event.target.name === 'ConfirmPassword' || event.target.name === 'Password') content.querySelector('[name="ConfirmPassword"]')?.setCustomValidity('');
    });
    modalElement.addEventListener('show.bs.modal', () => document.body.classList.add('coco-auth-open'));
    modalElement.addEventListener('shown.bs.modal', focusContent);
    modalElement.addEventListener('hide.bs.modal', event => {
        if (submitting) event.preventDefault();
    });
    modalElement.addEventListener('hidden.bs.modal', () => {
        document.body.classList.remove('coco-auth-open');
        loading?.abort();
        if (pageContent) {
            const pane = content.querySelector('[data-auth-pane]');
            if (pane) {
                pageContent.replaceChildren(pane);
                page.dataset.authPage = pane.dataset.authPane;
            }
            page.classList.remove('auth-page-modal-open');
        } else content.replaceChildren();
        previousFocus?.focus();
    });

    if (pageContent?.querySelector('[data-auth-pane]')) {
        const pane = pageContent.querySelector('[data-auth-pane]');
        baseReturnUrl = localUrl(pane.dataset.authReturnUrl);
        content.replaceChildren(pane);
        page.classList.add('auth-page-modal-open');
        modal.show();
    }
})();
