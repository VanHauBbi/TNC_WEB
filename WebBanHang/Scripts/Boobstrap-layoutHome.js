const loginContainer = document.querySelector('.login-container');

function toggleLogin() {
    loginContainer.style.display = (loginContainer.style.display === 'none' || loginContainer.style.display === '') ? 'block' : 'none';
}

document.addEventListener('click', function (event) {
    const target = event.target;
    const isLoginContainer = loginContainer.contains(target);
    const isAccountLink = target.closest('a[href="javascript:void(0);"]');

    if (!isLoginContainer && !isAccountLink) {
        loginContainer.style.display = 'none';
    }
});

const closeButton = document.querySelector('.close-btn-login');
if (closeButton) {
    closeButton.addEventListener('click', closeLogin);
}

function closeLogin(event) {
    event.stopPropagation();
    loginContainer.style.display = 'none';
}