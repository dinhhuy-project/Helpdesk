export const reportNumber = (value: number) => value.toLocaleString('vi-VN', { maximumFractionDigits: 2 })
export const reportPercent = (value: number | null) => value === null ? 'Chưa có mẫu đo' : `${reportNumber(value)}%`
