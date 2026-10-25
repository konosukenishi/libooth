use crate::Result;
use url::Host;
use wry::cookie::Cookie;

pub(crate) fn normalize_domain(domain: impl AsRef<str>) -> Result<String> {
    let domain = domain
        .as_ref()
        .trim_start_matches('.')
        .trim_end_matches('.');

    let Host::Domain(domain) = Host::parse(domain)? else {
        return Err("expected a DNS domain".into());
    };

    if domain.split('.').any(|label| {
        label.is_empty()
            || !label
                .bytes()
                .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-')
    }) {
        return Err("invalid cookie domain".into());
    }

    Ok(domain)
}

pub(super) fn matches_domain(cookie: &Cookie<'_>, domain: &str, include_subdomains: bool) -> bool {
    cookie.domain().is_some_and(|cookie_domain| {
        let cookie_domain = cookie_domain.to_ascii_lowercase();

        cookie_domain == domain
            || (include_subdomains
                && cookie_domain
                    .strip_suffix(domain)
                    .is_some_and(|prefix| prefix.ends_with('.')))
    })
}
