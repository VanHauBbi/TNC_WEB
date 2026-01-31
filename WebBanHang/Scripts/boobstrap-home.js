const productList = document.querySelector('.product-list');
const nextButton = document.getElementById('next-btn');
const prevButton = document.getElementById('prev-btn');

let currentOffset = 0;
const productWidth = document.querySelector('.product').offsetWidth + 20; // Bao gồm cả margin của sản phẩm
const visibleProducts = 4; // Hiển thị 4 sản phẩm

// Tính toán số lượng tối đa có thể cuộn dựa trên số sản phẩm
const maxOffset = -(productList.scrollWidth - productList.offsetWidth);

// Sự kiện khi nhấn nút ">"
nextButton.addEventListener('click', () => {
    if (currentOffset > maxOffset) {
        currentOffset -= productWidth * visibleProducts;
        productList.style.transform = `translateX(${currentOffset}px)`;
    }
});

// Sự kiện khi nhấn nút "<"
prevButton.addEventListener('click', () => {
    if (currentOffset < 0) {
        currentOffset += productWidth * visibleProducts;
        productList.style.transform = `translateX(${currentOffset}px)`;
    }
});

// Hàm đếm ngược thời gian
function startCountdown(duration) {
    let timer = duration, hours, minutes, seconds;
    const countdownElement = document.getElementById('countdown');
    setInterval(() => {
        hours = parseInt(timer / 3600, 10);
        minutes = parseInt((timer % 3600) / 60, 10);
        seconds = parseInt(timer % 60, 10);

        countdownElement.textContent = `${hours}:${minutes < 10 ? "0" : ""}${minutes}:${seconds < 10 ? "0" : ""}${seconds}`;

        if (--timer < 0) {
            timer = duration; // Reset thời gian nếu cần
        }
    }, 1000);
}

// Bắt đầu đếm ngược 12 tiếng (43200 giây)
startCountdown(43200);





