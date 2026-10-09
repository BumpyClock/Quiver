#![windows_subsystem = "windows"]

use std::{
    env,
    io::{self, Read},
    process::Command,
};

use byteorder::{NativeEndian, ReadBytesExt};
use serde::{Deserialize, Serialize};

const MAX_MESSAGE_BYTES: u32 = 1024 * 1024;

#[derive(Deserialize, Serialize)]
pub struct NativeMessage {
    pub url: String,
}

fn main() {
    // Try get the url from browser through native messaging
    let native_msg_url = match read_input(io::stdin()) {
        Ok(val) => {
            let json_val: Result<NativeMessage, _> = serde_json::from_slice(&val);
            match json_val {
                Ok(val) => Some(val.url),
                Err(_) => None,
            }
        }
        Err(_) => None,
    };

    let quiver_exe_path = {
        let current_exe_path = env::current_exe().unwrap();
        let current_dir = current_exe_path.parent().unwrap();
        current_dir.join("Quiver.exe")
    };

    let args = env::args().collect::<Vec<String>>();
    let trimed_args = &args[1..];

    let args = match native_msg_url {
        Some(url) => vec![String::from("--uri"), url],
        None => trimed_args.to_vec(),
    };
    let _ = Command::new(quiver_exe_path).args(args).spawn();
}

pub fn read_input<R: Read>(mut input: R) -> io::Result<Vec<u8>> {
    let len = input.read_u32::<NativeEndian>()?;
    if len > MAX_MESSAGE_BYTES {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "Native message exceeds 1 MiB limit",
        ));
    }

    let mut buffer = vec![0; len as usize];
    input.read_exact(&mut buffer)?;
    Ok(buffer)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_valid_frame() {
        let payload = br#"{"url":"https://example.com"}"#;
        let mut frame = (payload.len() as u32).to_ne_bytes().to_vec();
        frame.extend_from_slice(payload);
        let message: NativeMessage =
            serde_json::from_slice(&read_input(frame.as_slice()).unwrap()).unwrap();
        assert_eq!(message.url, "https://example.com");
    }

    #[test]
    fn rejects_malformed_payload() {
        let payload = b"{bad json}";
        let mut frame = (payload.len() as u32).to_ne_bytes().to_vec();
        frame.extend_from_slice(payload);
        let body = read_input(frame.as_slice()).unwrap();
        assert!(serde_json::from_slice::<NativeMessage>(&body).is_err());
    }

    #[test]
    fn rejects_truncated_header() {
        let err = read_input([1, 0].as_slice()).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::UnexpectedEof);
    }

    #[test]
    fn rejects_oversized_frame_before_reading_body() {
        let frame = (MAX_MESSAGE_BYTES + 1).to_ne_bytes();
        let err = read_input(frame.as_slice()).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::InvalidData);
    }

    #[test]
    fn rejects_truncated_body() {
        let mut frame = 5_u32.to_ne_bytes().to_vec();
        frame.extend_from_slice(b"abc");
        let err = read_input(frame.as_slice()).unwrap_err();
        assert_eq!(err.kind(), io::ErrorKind::UnexpectedEof);
    }

    #[test]
    fn accepts_maximum_size() {
        let mut frame = MAX_MESSAGE_BYTES.to_ne_bytes().to_vec();
        frame.extend(vec![b'x'; MAX_MESSAGE_BYTES as usize]);
        assert_eq!(
            read_input(frame.as_slice()).unwrap().len(),
            MAX_MESSAGE_BYTES as usize
        );
    }
}
