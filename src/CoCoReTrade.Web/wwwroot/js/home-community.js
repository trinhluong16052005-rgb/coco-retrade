(() => {
    'use strict';
    const gallery = document.querySelector('[data-coco-giving]');
    if (!gallery) return;
    const belt = gallery.querySelector('[data-giving-belt]');
    const original = gallery.querySelector('[data-giving-original]');
    const toggle = gallery.querySelector('[data-giving-toggle]');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let manualPause = false;

    const resize = () => {
        belt.querySelectorAll('[data-giving-copy]').forEach(copy => copy.remove());
        gallery.classList.toggle('has-motion', !reducedMotion.matches);
        if (reducedMotion.matches) {
            gallery.classList.remove('is-ready');
            toggle.hidden = true;
            return;
        }
        const distance = original.getBoundingClientRect().width;
        if (!distance) return;
        belt.style.setProperty('--giving-distance', `${distance}px`);
        belt.style.setProperty('--giving-duration', `${original.children.length * 5000}ms`);
        // Enough identical sequences to cover even ultrawide screens at the seam.
        const copies = Math.max(1, Math.ceil(gallery.clientWidth / distance));
        for (let index = 0; index < copies; index++) {
            const copy = original.cloneNode(true);
            copy.removeAttribute('data-giving-original');
            copy.dataset.givingCopy = '';
            copy.setAttribute('aria-hidden', 'true');
            copy.querySelectorAll('a').forEach(link => link.tabIndex = -1);
            copy.querySelectorAll('img').forEach(img => { img.alt = ''; img.loading = 'eager'; });
            belt.append(copy);
        }
        original.querySelectorAll('img').forEach(img => { img.loading = 'eager'; });
        gallery.classList.add('is-ready');
        toggle.hidden = false;
    };

    toggle.addEventListener('click', () => {
        manualPause = !manualPause;
        gallery.dataset.paused = String(manualPause);
        toggle.setAttribute('aria-pressed', String(manualPause));
        toggle.setAttribute('aria-label', manualPause ? 'Tiếp tục chuyển động ảnh' : 'Tạm dừng chuyển động ảnh');
    });
    gallery.addEventListener('focusin', event => {
        if (!original.contains(event.target) || reducedMotion.matches) return;
        gallery.scrollLeft = 0;
        const link = event.target.closest('a');
        const animation = belt.getAnimations()[0];
        if (!link || !animation) return;
        const distance = original.getBoundingClientRect().width;
        const offset = link.getBoundingClientRect().left - original.getBoundingClientRect().left;
        const travel = Math.max(0, Math.min(distance - gallery.clientWidth, offset - 8));
        animation.currentTime = travel / distance * Number(animation.effect.getTiming().duration);
    });
    reducedMotion.addEventListener('change', resize);
    if ('ResizeObserver' in window) new ResizeObserver(resize).observe(gallery);
    else window.addEventListener('resize', resize, { passive:true });
    resize();
})();

(() => {
    'use strict';
    const feed = document.querySelector('[data-coco-tiktok]');
    if (!feed) return;
    const panel = feed.closest('.coco-tiktok-panel');
    const preview = feed.querySelector('[data-tiktok-preview]');
    const mount = feed.querySelector('[data-tiktok-mount]');
    const status = panel.querySelector('[data-tiktok-status]');
    const retry = panel.querySelector('[data-tiktok-retry]');
    if (!preview || !mount || !status || !retry) return;
    const origin = 'https://www.tiktok.com';
    const frameName = '__tt_embed__cocoProfile';
    let frame = null;
    let timeout;

    const slow = () => {
        feed.removeAttribute('aria-busy');
        status.hidden = false;
        status.textContent = 'TikTok đang tải chậm. Bạn vẫn có thể mở các bài đăng hoặc ghé tài khoản CoCo.';
        retry.hidden = false;
    };

    const load = (restart = false) => {
        if (frame && !restart) return;
        clearTimeout(timeout);
        mount.replaceChildren();
        mount.inert = true;
        mount.setAttribute('aria-hidden', 'true');
        feed.classList.remove('is-ready');
        feed.style.removeProperty('--tiktok-height');
        feed.setAttribute('aria-busy', 'true');
        preview.hidden = false;
        status.hidden = true;
        retry.hidden = true;
        frame = document.createElement('iframe');
        frame.name = frameName;
        frame.title = 'Tài khoản TikTok CoCo Kho Ký Gửi — các bài đăng mới nhất';
        frame.tabIndex = -1;
        frame.allow = 'fullscreen';
        frame.allowFullscreen = true;
        frame.src = `${origin}/embed/@cocokhokygui.dn?lang=vi&embedFrom=oembed`;
        const currentFrame = frame;
        const failed = () => {
            if (frame === currentFrame && !feed.classList.contains('is-ready')) slow();
        };
        frame.addEventListener('error', failed, { once: true });
        mount.append(frame);
        // Keep real local account previews usable while the external feed loads.
        // A late successful response can still reveal the feed after this notice.
        timeout = setTimeout(failed, 10000);
    };

    window.addEventListener('message', event => {
        if (!frame || event.origin !== origin || event.source !== frame.contentWindow) return;
        let data = event.data;
        if (typeof data === 'string') {
            try { data = JSON.parse(data); } catch { return; }
        }
        if (!data || typeof data !== 'object' || data.signalSource !== frameName) return;
        const height = Number(data.height);
        if (!Number.isFinite(height) || height < 250 || height > 1200) return;
        clearTimeout(timeout);
        const restoreFocus = preview.contains(document.activeElement) || document.activeElement === retry;
        feed.style.setProperty('--tiktok-height', `${Math.ceil(height)}px`);
        feed.classList.add('is-ready');
        feed.removeAttribute('aria-busy');
        mount.inert = false;
        mount.removeAttribute('aria-hidden');
        frame.tabIndex = 0;
        preview.hidden = true;
        status.hidden = true;
        retry.hidden = true;
        if (restoreFocus) frame.focus();
    });

    retry.addEventListener('click', () => load(true));
    // Start during early idle time instead of waiting until this bottom section is reached.
    // Direct official creator iframe avoids embed.js and its extra bootstrap requests.
    if ('requestIdleCallback' in window) window.requestIdleCallback(() => load(), { timeout: 1500 });
    else setTimeout(() => load(), 400);
})();
