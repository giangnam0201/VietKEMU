"""Real mobile browser -> native HTTP -> Windows queue, using synthetic search data."""
import os
import sys
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
    if os.environ.get('VIETK_REMOTE_TEST_PHASE') == 'original':
        page.locator('#queueTab').click()
        expect(page.locator('#queueBank')).to_have_value('original')
        expect(page.locator('#rows .row')).to_have_count(4)
        expect(page.locator('#rows .row').nth(3)).to_contain_text('45%')
        page.locator('#rows .row').nth(2).get_by_role('button', name='Ưu tiên', exact=True).click()
        expect(page.locator('#rows .row').nth(1)).to_contain_text('Original fixture B')
        page.locator('#queueBank').select_option('youtube')
        expect(page.locator('#rows .row')).to_have_count(2)
        page.locator('#queueBank').select_option('original')
        expect(page.locator('#rows .row')).to_have_count(4)
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Original queue page overflows'
        page.screenshot(path=str(output / 'phone-original-queue.png'), full_page=True)
        assert not errors, 'Original queue JavaScript errors'
        browser.close()
        print('Original native queue browser selection, priority, download progress and source isolation verified.')
        sys.exit(0)
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
    page.locator('#defaultVolumeSettings summary').click()
    expect(page.locator('#defaultVolumeSaved')).to_have_text('Đã lưu: 7/20')
    expect(page.locator('#defaultVolume')).to_be_enabled()
    page.locator('#defaultVolume').focus()
    page.locator('#defaultVolume').press('Home')
    for _ in range(6):
        page.locator('#defaultVolume').press('ArrowRight')
    expect(page.locator('#defaultVolumeDraft')).to_have_text('6')
    assert page.evaluate("async()=> (await api('settings/default-volume')).defaultVolume") == 7, 'Slider saved before confirmation'
    page.locator('#defaultVolumeConfirm').click()
    expect(page.locator('#defaultVolumeSaved')).to_have_text('Đã lưu: 6/20')
    expect(page.locator('#defaultVolume')).to_be_enabled()
    page.locator('#defaultVolumeMinus').click()
    expect(page.locator('#defaultVolumeDraft')).to_have_text('5')
    page.locator('#defaultVolumeCancel').click()
    expect(page.locator('#defaultVolumeDraft')).to_have_text('6')
    expect(page.locator('#defaultVolume')).to_be_enabled()
    expect(page.locator('#volume')).to_have_text(f'Âm lượng {volume}/20')
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Default-volume mobile editor overflows'
    page.screenshot(path=str(output / 'phone-default-volume.png'), full_page=True)
    page.locator('#defaultVolumePlus').click()
    page.locator('#defaultVolumeConfirm').click()
    expect(page.locator('#defaultVolumeSaved')).to_have_text('Đã lưu: 7/20')
    page.locator('#defaultVolumeSettings summary').click()
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Mobile page overflows horizontally'
    page.screenshot(path=str(output / 'phone-queue.png'), full_page=True)
    page.locator('#songsTab').click()
    page.screenshot(path=str(output / 'phone-search.png'), full_page=True)
    assert not errors, 'Mobile page JavaScript errors'
    browser.close()
print('Real phone-sized Chromium pairing, search, queue, staged default-volume save/cancel and layout verified.')
