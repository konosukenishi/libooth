use crate::Result;
use sdl3::sys::events::{
    SDL_EVENT_FIRST, SDL_EVENT_LAST, SDL_EVENT_WINDOW_CLOSE_REQUESTED, SDL_Event, SDL_GETEVENT,
    SDL_PeepEvents,
};
use sdl3::video::Window;
use sdl3::{EventSubsystem, VideoSubsystem};
use std::cell::RefCell;
use std::rc::Rc;
use url::Url;
use wry::{PageLoadEvent, WebView, WebViewBuilder};

pub(super) struct Browser {
    webview: WebView,
    window: Window,
    event_subsystem: EventSubsystem,
    video_subsystem: VideoSubsystem,
    history: Rc<RefCell<Vec<Url>>>,
    #[cfg(target_os = "windows")]
    _web_context: wry::WebContext,
}

impl Browser {
    pub(super) fn open(url: &Url) -> Result<Self> {
        sdl3::hint::set("SDL_VIDEO_ALLOW_SCREENSAVER", "1");

        let sdl = sdl3::init()?;

        let video_subsystem = sdl.video()?;
        let event_subsystem = sdl.event()?;

        let mut window = video_subsystem
            .window("WebView", 480, 720)
            .position_centered()
            .build()?;

        let history = Rc::new(RefCell::new(Vec::new()));

        let page_load = Rc::clone(&history);
        // Unity is commonly installed under Program Files. WebView2's default
        // executable-relative data directory is not writable there.
        #[cfg(target_os = "windows")]
        let mut web_context = {
            let root = std::env::var_os("LOCALAPPDATA").ok_or("LOCALAPPDATA is unavailable")?;
            let directory = std::path::PathBuf::from(root)
                .join("dev.konosuke.libooth")
                .join("WebView2");
            std::fs::create_dir_all(&directory)?;
            wry::WebContext::new(Some(directory))
        };
        #[cfg(target_os = "windows")]
        let builder = WebViewBuilder::new_with_web_context(&mut web_context);
        #[cfg(not(target_os = "windows"))]
        let builder = WebViewBuilder::new();

        let webview = builder
            .with_incognito(true)
            .with_url(url.as_str())
            .with_on_page_load_handler(move |event, url| {
                if matches!(event, PageLoadEvent::Finished) {
                    if let Ok(url) = Url::parse(&url) {
                        page_load.borrow_mut().push(url);
                    }
                }
            })
            .build(&window)?;

        window.raise();

        Ok(Self {
            webview,
            window,
            event_subsystem,
            video_subsystem,
            history,
            #[cfg(target_os = "windows")]
            _web_context: web_context,
        })
    }

    pub(super) fn window(&self) -> &Window {
        &self.window
    }

    pub(super) fn webview(&self) -> &WebView {
        &self.webview
    }

    pub(super) fn history(&self) -> Vec<Url> {
        self.history.borrow().clone()
    }
}

pub fn poll_close_requests() -> Result<Vec<u32>> {
    // SDL_PeepEvents alone does not pump the Windows messages used by SDL and
    // WebView2. The macOS host continues to own its existing Cocoa event loop.
    #[cfg(target_os = "windows")]
    unsafe {
        sdl3::sys::events::SDL_PumpEvents();
    }

    let mut requested_ids = Vec::new();

    for _ in 0..256 {
        let mut event = SDL_Event::default();

        let count = unsafe {
            SDL_PeepEvents(
                &mut event,
                1,
                SDL_GETEVENT,
                SDL_EVENT_FIRST.0,
                SDL_EVENT_LAST.0,
            )
        };

        if count < 0 {
            return Err(sdl3::get_error().into());
        }

        if count == 0 {
            return Ok(requested_ids);
        }

        if unsafe { event.r#type } == SDL_EVENT_WINDOW_CLOSE_REQUESTED.0 {
            requested_ids.push(unsafe { event.window.windowID.0 });
        }
    }

    Ok(requested_ids)
}
