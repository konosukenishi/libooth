mod browser;
mod cookies;

use crate::browser::Browser;
use serde_json::json;
use std::cell::RefCell;
use std::collections::BTreeMap;
use std::ffi::{CStr, CString, c_char};
use std::panic::{AssertUnwindSafe, catch_unwind};
use url::Url;

type Result<T> = std::result::Result<T, Box<dyn std::error::Error>>;

thread_local! {
    static BROWSERS: RefCell<BTreeMap<u32, Browser>> = const { RefCell::new(BTreeMap::new()) };
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_open(url: *const c_char) -> u32 {
    ffi_boundary(|| {
        let url = Url::parse(&unsafe { copy_c_string(url)? })?;
        require_https(&url)?;

        let browser = Browser::open(&url)?;

        BROWSERS.with(|cell| {
            let mut browsers = cell
                .try_borrow_mut()
                .map_err(|_| "browsers are already borrowed")?;
            let id = browser.window().id();

            browsers.insert(id, browser);

            Ok(id)
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_close(id: u32) {
    ffi_boundary(|| {
        BROWSERS.with(|cell| {
            let browser = {
                let mut browsers = cell
                    .try_borrow_mut()
                    .map_err(|_| "browsers are already borrowed")?;
                browsers.remove(&id)
            };

            drop(browser);
            Ok(())
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_close_all() {
    ffi_boundary(|| {
        BROWSERS.with(|cell| {
            let browsers = std::mem::take(
                &mut *cell
                    .try_borrow_mut()
                    .map_err(|_| "browsers are already borrowed")?,
            );

            drop(browsers);
            Ok(())
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_process_events() -> bool {
    ffi_boundary(|| {
        BROWSERS.with(|cell| {
            let removed = {
                let mut browsers = cell
                    .try_borrow_mut()
                    .map_err(|_| "browsers are already borrowed")?;

                if browsers.is_empty() {
                    return Ok(true);
                }

                browser::poll_close_requests()?
                    .into_iter()
                    .filter_map(|id| browsers.remove(&id))
                    .collect::<Vec<_>>()
            };

            drop(removed);
            Ok(true)
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_get_history(id: u32) -> *mut c_char {
    with_browser(id, |browser| {
        let history: Vec<_> = browser.history().into_iter().map(String::from).collect();
        into_c_string(json!({"history": history}).to_string())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_get_cookies(
    id: u32,
    domain: *const c_char,
    include_subdomains: bool,
) -> *mut c_char {
    with_browser(id, |browser| {
        let domain = cookies::normalize_domain(unsafe { copy_c_string(domain)? })?;

        let cookies: Vec<_> = browser
            .webview()
            .cookies()?
            .into_iter()
            .filter(|cookie| cookies::matches_domain(cookie, &domain, include_subdomains))
            .map(|cookie| {
                let expires = cookie.expires_datetime().map(|date| date.unix_timestamp());

                json!({
                    "name": cookie.name(),
                    "value": cookie.value(),
                    "domain": cookie.domain().unwrap_or(""),
                    "path": cookie.path().unwrap_or("/"),
                    "secure": cookie.secure().unwrap_or(false),
                    "httpOnly": cookie.http_only().unwrap_or(false),
                    "session": expires.is_none(),
                    "expires": expires.unwrap_or(0),
                })
            })
            .collect();

        into_c_string(json!({"cookies": cookies}).to_string())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_navigate(id: u32, url: *const c_char) -> bool {
    with_browser(id, |browser| {
        let url = Url::parse(&unsafe { copy_c_string(url)? })?;
        require_https(&url)?;

        browser.webview().load_url(url.as_str())?;
        Ok(true)
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn webview_is_open(id: u32) -> bool {
    ffi_boundary(|| {
        BROWSERS.with(|cell| {
            Ok(cell
                .try_borrow()
                .map_err(|_| "browsers are already borrowed")?
                .contains_key(&id))
        })
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn webview_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        drop(unsafe { CString::from_raw(ptr) })
    }
}

fn ffi_boundary<T: Default>(action: impl FnOnce() -> Result<T>) -> T {
    let result = catch_unwind(AssertUnwindSafe(action))
        .unwrap_or_else(|_| Err("native operation panicked".into()));

    result.unwrap_or_else(|error| {
        eprintln!("webview error: {error}");
        T::default()
    })
}

fn with_browser<T: Default>(id: u32, action: impl FnOnce(&Browser) -> Result<T>) -> T {
    ffi_boundary(|| {
        BROWSERS.with(|cell| {
            let browsers = cell
                .try_borrow()
                .map_err(|_| "browsers are already borrowed")?;
            let browser = browsers.get(&id).ok_or("invalid browser ID")?;
            action(browser)
        })
    })
}

fn into_c_string(value: String) -> Result<*mut c_char> {
    Ok(CString::new(value)?.into_raw())
}

unsafe fn copy_c_string(ptr: *const c_char) -> Result<String> {
    if ptr.is_null() {
        return Err("string pointer is null".into());
    }
    Ok(unsafe { CStr::from_ptr(ptr) }.to_str()?.to_owned())
}

fn require_https(url: &Url) -> Result<()> {
    if url.scheme() != "https" || url.host_str().is_none() {
        return Err("expected an absolute HTTPS URL with a host".into());
    }

    Ok(())
}
