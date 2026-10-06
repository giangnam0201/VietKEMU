"""Phone browser idle-playlist edits against the real paired native server."""
from playwright.sync_api import expect


def verify(page, output, volume):
    settings = page.locator('#playlistSettings')
    settings.locator('summary').click()
    expect(page.locator('#playlistRows .row')).to_have_count(1)
    expect(page.locator('#playlistRows .row')).to_contain_text('Playlist fixture A')
    expect(page.locator('#playlistCreate')).to_be_enabled()
    page.locator('#playlistCreate').click()
    page.locator('#playlistQuery').fill('Page')
    expect(page.locator('#playlistSearchRows button')).to_have_count(50)
    page.locator('#playlistSearchResults').evaluate('element => element.scrollTop = element.scrollHeight')
    expect(page.locator('#playlistSearchRows button')).to_have_count(60)
    assert page.locator('#playlistSearchRows button').last.get_attribute('data-songid') == '101159'
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Playlist search overflows horizontally'
    page.screenshot(path=str(output / 'phone-playlist-search.png'), full_page=True)
    page.locator('#playlistQuery').fill('<img')
    page.locator('#playlistSearchRows [data-songid="101003"]').click()
    expect(page.locator('#playlistAddedRows .row')).to_have_count(1)
    expect(page.locator('#playlistAddedRows h3')).to_have_text('<img src=x onerror=alert(1)>')
    assert page.locator('#playlistAddedRows img').count() == 0, 'Song name was interpreted as HTML'
    expect(page.locator('#playlistQuery')).to_have_value('<img')
    page.locator('#playlistAddedRows button').click()
    expect(page.locator('#playlistAddedRows .row')).to_have_count(0)
    page.locator('#playlistBack').click()
    expect(page.locator('#playlistRows .row')).to_have_count(1)
    assert page.evaluate("async()=> (await api('settings/broadcast-playlist/list')).publishMusicOfLocal.map(s=>s.songid)") == [101001]

    def add_duplicate_b():
        page.locator('#playlistCreate').click()
        expect(page.locator('#playlistQuery')).to_have_value('')
        expect(page.locator('#playlistAddedRows .row')).to_have_count(0)
        page.locator('#playlistQuery').fill('Playlist fixture B')
        page.locator('#playlistSearchRows [data-songid="101002"]').click()
        page.locator('#playlistQuery').fill('Playlist fixture')
        page.locator('#playlistSearchRows [data-songid="101002"]').click()
        expect(page.locator('#playlistAddedRows .row')).to_have_count(2)
        page.screenshot(path=str(output / 'phone-playlist-add-draft.png'), full_page=True)
        page.locator('#playlistAddConfirm').click()
        expect(page.locator('#playlistRows .row')).to_have_count(3)
        assert page.evaluate("async()=> (await api('settings/broadcast-playlist/list')).publishMusicOfLocal.map(s=>s.songid)") == [101001], 'Add confirmation saved parent draft early'
        page.locator('#playlistRows .row').nth(2).get_by_role('button', name='Tốp', exact=True).click()
        expect(page.locator('#playlistRows .row').nth(0)).to_contain_text('Playlist fixture B')
        expect(page.locator('#playlistRows .row').nth(1)).to_contain_text('Playlist fixture A')
        page.locator('#playlistRows .row').nth(1).get_by_role('button', name='Xóa', exact=True).click()
        expect(page.locator('#playlistRows .row')).to_have_count(2)

    add_duplicate_b()
    page.locator('#playlistCancel').click()
    expect(settings).not_to_have_attribute('open', '')
    assert page.evaluate("async()=> (await api('settings/broadcast-playlist/list')).publishMusicOfLocal.map(s=>s.songid)") == [101001], 'Cancel saved reordered/deleted parent draft'
    settings.locator('summary').click()
    expect(page.locator('#playlistRows .row')).to_have_count(1)
    expect(page.locator('#playlistCreate')).to_be_enabled()
    add_duplicate_b()
    page.screenshot(path=str(output / 'phone-playlist-editor.png'), full_page=True)
    page.locator('#playlistConfirm').click()
    page.wait_for_function("async()=> JSON.stringify((await api('settings/broadcast-playlist/list')).publishMusicOfLocal.map(s=>s.songid)) === '[101002,101002]'")
    assert page.evaluate("async()=> (await api('settings/broadcast-playlist')).publishMusicOfLocal.map(s=>s.songid)") == [101002], 'Runtime list retained raw duplicates'
    expect(page.locator('#pause')).to_have_text('Tiếp tục')
    expect(page.locator('#volume')).to_have_text(f'Âm lượng {volume}/20')
    settings.locator('summary').click()
    expect(page.locator('#playlistRows .row')).to_have_count(2)
    page.locator('#playlistRows .row').nth(1).get_by_role('button', name='Xóa', exact=True).click()
    page.locator('#playlistRows .row').nth(0).get_by_role('button', name='Xóa', exact=True).click()
    expect(page.locator('#playlistEmpty')).to_be_visible()
    page.locator('#playlistConfirm').click()
    page.wait_for_function("async()=> (await api('settings/broadcast-playlist/list')).publishMusicOfLocal.length === 0")
    settings.locator('summary').click()
    expect(page.locator('#playlistEmpty')).to_be_visible()
    page.screenshot(path=str(output / 'phone-playlist-empty.png'), full_page=True)
    page.locator('#playlistCancel').click()
    page.evaluate("async()=> await api('settings/broadcast-playlist',{publishMusicOfLocal:[{songid:101001,songname:'',singername:''}]})")
    expect(page.locator('#pause')).to_have_text('Tiếp tục')
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Playlist editor overflows horizontally'
