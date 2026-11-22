(() => {
    'use strict';
    const documentRoot = document.querySelector('[data-policy-document]');
    if (!documentRoot) return;
    const links = [...documentRoot.querySelectorAll('.policy-toc a[href^="#"]')];
    const sections = links.map(link => document.getElementById(link.hash.slice(1)));
    if (!links.length || sections.some(section => !section)) return;
    let scheduled = false;
    const update = () => {
        scheduled = false;
        const header = document.querySelector('.site-header');
        const threshold = (header?.getBoundingClientRect().height || 80) + 48;
        let active = 0;
        sections.forEach((section, index) => { if (section.getBoundingClientRect().top <= threshold) active = index; });
        if (window.scrollY > 0 && window.scrollY + window.innerHeight >= document.documentElement.scrollHeight - 2)
            active = sections.length - 1;
        links.forEach((link, index) => {
            if (index === active) link.setAttribute('aria-current', 'location');
            else link.removeAttribute('aria-current');
        });
    };
    const schedule = () => {
        if (scheduled) return;
        scheduled = true;
        requestAnimationFrame(update);
    };
    window.addEventListener('scroll', schedule, { passive:true });
    window.addEventListener('resize', schedule, { passive:true });
    window.addEventListener('hashchange', schedule);
    document.fonts.ready.then(update);
})();
