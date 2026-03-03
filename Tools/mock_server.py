"""
简易 Mock 服务器，用于验证 Bug Reporter SDK 的上报数据。
运行: python Tools/mock_server.py
"""
from http.server import HTTPServer, BaseHTTPRequestHandler
import cgi
import json
import os
from datetime import datetime


class BugReportHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path != "/api/bug-report":
            self.send_response(404)
            self.end_headers()
            return

        content_type = self.headers.get("Content-Type", "")
        if "multipart/form-data" not in content_type:
            self.send_response(400)
            self.end_headers()
            self.wfile.write(b"Expected multipart/form-data")
            return

        form = cgi.FieldStorage(
            fp=self.rfile,
            headers=self.headers,
            environ={"REQUEST_METHOD": "POST",
                     "CONTENT_TYPE": content_type},
        )

        print(f"\n{'='*60}")
        print(f"[{datetime.now():%H:%M:%S}] Bug Report Received")
        print(f"{'='*60}")

        fields = {}
        files = []
        for key in form.keys():
            item = form[key]
            if item.filename:
                save_dir = "uploads"
                os.makedirs(save_dir, exist_ok=True)
                path = os.path.join(save_dir, item.filename)
                with open(path, "wb") as f:
                    f.write(item.file.read())
                files.append(f"{key}: {item.filename} (saved to {path})")
            else:
                fields[key] = item.value

        for k, v in sorted(fields.items()):
            display = v[:200] + "..." if len(v) > 200 else v
            print(f"  {k}: {display}")
        for f in files:
            print(f"  [FILE] {f}")
        print(f"{'='*60}\n")

        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps({"status": "ok"}).encode())


if __name__ == "__main__":
    port = 8000
    server = HTTPServer(("0.0.0.0", port), BugReportHandler)
    print(f"Mock server running on http://localhost:{port}/api/bug-report")
    print("Waiting for bug reports...\n")
    server.serve_forever()
