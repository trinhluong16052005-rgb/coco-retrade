(() => {
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)');

  function bindProductPhoto(frame) {
    const source = frame.dataset.secondarySrc;
    if (!source) return;
    const card = frame.closest('.product-card, .sign-product, .checkout-cross-sell article') || frame;
    let hovered = false;
    let loading;

    card.addEventListener('pointerenter', async event => {
      if (!finePointer.matches || reducedMotion.matches || event.pointerType === 'touch') return;
      hovered = true;
      if (!loading) {
        const alternate = new Image();
        alternate.className = 'product-image-alternate';
        alternate.alt = '';
        alternate.setAttribute('aria-hidden', 'true');
        alternate.decoding = 'async';
        alternate.src = source;
        loading = alternate.decode().then(() => {
          frame.append(alternate);
          return true;
        }, () => false);
      }
      if (await loading && hovered && finePointer.matches && !reducedMotion.matches)
        frame.classList.add('is-alternate-visible');
    });

    card.addEventListener('pointerleave', () => {
      hovered = false;
      frame.classList.remove('is-alternate-visible');
    });
  }

  const bindProductPhotos = root => root.querySelectorAll('.product-image-frame[data-secondary-src]').forEach(bindProductPhoto);
  bindProductPhotos(document);
  const checkoutRecommendations = document.getElementById('checkoutRecommendations');
  checkoutRecommendations?.addEventListener('coco:recommendations-updated', () => bindProductPhotos(checkoutRecommendations));

  if (reducedMotion.matches || !('IntersectionObserver' in window)) return;

  const sections = [...document.querySelectorAll('#main-content section')].filter(section =>
    !section.parentElement.closest('section, dialog, [hidden]') &&
    section.getBoundingClientRect().top > window.innerHeight);

  const observer = new IntersectionObserver(entries => {
    for (const { isIntersecting, target } of entries) {
      if (!isIntersecting) continue;
      if (!reducedMotion.matches) target.classList.add('coco-section-visible');
      observer.unobserve(target);
    }
  }, { rootMargin: '0px 0px -24px' });

  sections.forEach(section => observer.observe(section));
})();
