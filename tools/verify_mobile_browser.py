"""Real mobile browser -> native HTTP -> Windows queue, using synthetic search data."""
import os
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

base = os.environ['VIETK_REMOTE_TEST_URL']
token = os.environ['VIETK_REMOTE_TEST_TOKEN']
output = Path(os.environ['VIETK_REMOTE_TEST_OUTPUT'])
with sync_playwright() as p:
    browser = p.chromium.launch()
    context = browser.new_context(viewport={'width': 390, 'height': 844}, is_mobile=True, has_touch=True)
    page = context.new_page()
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto(base + '#token=' + token)
    expect(page.locator('#connection')).to_have_text('Đã kết nối')
    assert '#token=' not in page.url
    page.locator('#query').fill('fixture')
    page.locator('#searchButton').click()
    expect(page.locator('#results .card')).to_have_count(1)
    page.get_by_role('button', name='Thêm bài', exact=True).click()
    expect(page.locator('#count')).to_have_text('3')
    page.locator('#queueTab').click()
    expect(page.locator('#rows .row')).to_have_count(3)
    row = page.locator('#rows .row').filter(has_text='Remote search result')
    row.get_by_role('button', name='Ưu tiên', exact=True).click()
    expect(page.locator('#rows .row').nth(1)).to_contain_text('Remote search result')
    page.locator('#rows .row').nth(1).get_by_role('button', name='↓', exact=True).click()
    expect(page.locator('#rows .row').nth(2)).to_contain_text('Remote search result')
    page.locator('#rows .row').nth(2).get_by_role('button', name='Xóa', exact=True).click()
    expect(page.locator('#count')).to_have_text('2')
    volume = int(page.locator('#volume').inner_text().split()[-1].split('/')[0])
    page.locator('[data-command=voldec]').click()
    expect(page.locator('#volume')).to_have_text(f'Âm lượng {max(0,volume-1)}/20')
    page.locator('[data-command=volinc]').click()
    expect(page.locator('#volume')).to_have_text(f'Âm lượng {volume}/20')
    page.locator('#mute').click()
    expect(page.locator('#mute')).to_have_text('Bật tiếng')
    page.locator('#mute').click()
    expect(page.locator('#mute')).to_have_text('Tắt tiếng')
    expect(page.locator('#vocal')).to_be_enabled()
    expect(page.locator('#vocal')).to_have_text('Ngắt lời')
    page.locator('#vocal').click()
    expect(page.locator('#vocal')).to_have_text('Nguyên xướng')
    page.locator('#vocal').click()
    expect(page.locator('#vocal')).to_have_text('Ngắt lời')
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile page overflows horizontally'
    page.screenshot(path=str(output / 'phone-queue.png'), full_page=True)
    page.locator('#songsTab').click()
    page.screenshot(path=str(output / 'phone-search.png'), full_page=True)
    assert not errors, 'Mobile page JavaScript errors'
    browser.close()
print('Real phone-sized Chromium pairing, search, add, priority, reorder, delete and layout verified.')
