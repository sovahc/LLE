import socket
import threading
from datetime import datetime


def handle_client(conn, addr):
    try:
        while True:
            msg = str(datetime.now().time()).encode() + b"\n"
            conn.sendall(msg)
            threading.Event().wait(1.0)
    except (BrokenPipeError, ConnectionResetError):
        pass
    finally:
        conn.close()


with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as srv:
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind(("127.0.0.1", 8080))
    srv.listen(5)
    print("Listening on 127.0.0.1:8080")

    while True:
        conn, addr = srv.accept()
        print(f"Connected {addr}")
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()
