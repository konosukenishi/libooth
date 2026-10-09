use keyring::{Entry, Error};
use std::ffi::{CStr, CString, c_char};
use std::panic::{UnwindSafe, catch_unwind};
use std::ptr;

const SERVICE: &str = "me.konosuke.libooth";

type Result<T> = std::result::Result<T, Box<dyn std::error::Error>>;

#[unsafe(no_mangle)]
pub unsafe extern "C" fn secret_get(key: *const c_char) -> *mut c_char {
    ffi_boundary(|| {
        let key = unsafe { copy_c_string(key)? };

        let entry = Entry::new(SERVICE, &key)?;

        let value = match entry.get_password() {
            Ok(value) => value,

            Err(Error::NoEntry) => return Ok(ptr::null_mut()),

            Err(error) => return Err(error.into()),
        };

        let value = CString::new(value)?;

        Ok(value.into_raw())
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn secret_set(key: *const c_char, value: *const c_char) -> bool {
    ffi_boundary(|| {
        let key = unsafe { copy_c_string(key)? };
        let value = unsafe { copy_c_string(value)? };

        let entry = Entry::new(SERVICE, &key)?;

        entry.set_password(&value)?;

        Ok(true)
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn secret_delete(key: *const c_char) -> bool {
    ffi_boundary(|| {
        let key = unsafe { copy_c_string(key)? };

        let entry = Entry::new(SERVICE, &key)?;

        match entry.delete_credential() {
            Ok(()) | Err(Error::NoEntry) => Ok(true),
            Err(error) => Err(error.into()),
        }
    })
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn secret_free_string(value: *mut c_char) {
    if !value.is_null() {
        drop(unsafe { CString::from_raw(value) })
    }
}

fn ffi_boundary<T: Default>(action: impl FnOnce() -> Result<T> + UnwindSafe) -> T {
    match catch_unwind(action) {
        Ok(Ok(value)) => value,
        Ok(Err(_)) => {
            eprintln!("secret store error: operation failed");
            T::default()
        }
        Err(_) => {
            eprintln!("secret store error: operation panicked");
            T::default()
        }
    }
}

unsafe fn copy_c_string(ptr: *const c_char) -> Result<String> {
    if ptr.is_null() {
        return Err("string pointer is null".into());
    }

    let value = unsafe { CStr::from_ptr(ptr) };

    Ok(value.to_str()?.to_owned())
}
