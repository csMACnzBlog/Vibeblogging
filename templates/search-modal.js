(function () {
    'use strict';

    var searchLoaded = false;

    function openModal() {
        var modal = document.getElementById('search-modal');
        var iframe = document.getElementById('search-iframe');

        if (!searchLoaded) {
            iframe.src = 'search.html';
            searchLoaded = true;
        }

        modal.removeAttribute('hidden');
        document.body.style.overflow = 'hidden';

        var closeBtn = modal.querySelector('.search-modal-close');
        if (closeBtn) {
            closeBtn.focus();
        }
    }

    function closeModal() {
        var modal = document.getElementById('search-modal');
        modal.setAttribute('hidden', '');
        document.body.style.overflow = '';

        var searchBtn = document.getElementById('search-btn');
        if (searchBtn) {
            searchBtn.focus();
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var searchBtn = document.getElementById('search-btn');
        var modal = document.getElementById('search-modal');
        var closeBtn = modal ? modal.querySelector('.search-modal-close') : null;
        var overlay = modal ? modal.querySelector('.search-modal-overlay') : null;

        if (searchBtn) {
            searchBtn.addEventListener('click', openModal);
        }

        if (closeBtn) {
            closeBtn.addEventListener('click', closeModal);
        }

        if (overlay) {
            overlay.addEventListener('click', closeModal);
        }

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && modal && !modal.hasAttribute('hidden')) {
                closeModal();
            }
        });
    });
}());
