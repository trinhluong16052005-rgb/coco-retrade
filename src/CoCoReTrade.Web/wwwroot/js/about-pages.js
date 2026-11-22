(() => {
    'use strict';
    const navigation = document.querySelector('.page-about .about-navigation');
    const current = navigation?.querySelector('[aria-current="page"]');
    if (!current) return;
    const revealCurrent = () => {
        if (navigation.scrollWidth <= navigation.clientWidth) return;
        const link = current.getBoundingClientRect();
        const viewport = navigation.getBoundingClientRect();
        if (link.left < viewport.left || link.right > viewport.right)
            navigation.scrollLeft += link.left - viewport.left - (viewport.width - link.width) / 2;
    };
    document.fonts.ready.then(revealCurrent);
    window.addEventListener('resize', revealCurrent, { passive:true });
})();
