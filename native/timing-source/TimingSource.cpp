#include <windows.h>
#include <d3d11_1.h>
#include <dxgi1_3.h>
#include <wrl/client.h>
#include <mmsystem.h>
#include <vector>
#include <cmath>
#include <cstdint>
#include <stdexcept>
using Microsoft::WRL::ComPtr;
namespace {
bool running=true;
void Check(HRESULT hr) { if (FAILED(hr)) throw std::runtime_error("GPU timing source initialization/presentation failed."); }
LRESULT CALLBACK Procedure(HWND window, UINT message, WPARAM a, LPARAM b) {
    if (message==WM_DESTROY) { running=false; PostQuitMessage(0); return 0; }
    return DefWindowProcW(window,message,a,b);
}
std::uint32_t Clock() {
    FILETIME ft{}; GetSystemTimePreciseAsFileTime(&ft); ULARGE_INTEGER value{}; value.LowPart=ft.dwLowDateTime; value.HighPart=ft.dwHighDateTime;
    return static_cast<std::uint32_t>((value.QuadPart-116444736000000000ULL)/10000);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int) {
    HWND window{}; HWAVEOUT audio{}; WAVEHDR buffer{}; HANDLE timer{};
    std::vector<short> pcm(96000);
    try {
        WNDCLASSW type{}; type.lpfnWndProc=Procedure; type.hInstance=instance; type.lpszClassName=L"JKGpuTimingFixture"; RegisterClassW(&type);
        // Actual 1920x1080 client pixels, with no title-bar crop or upscale.
        window=CreateWindowW(type.lpszClassName,L"JKiller GPU timing fixture",WS_POPUP,40,40,1920,1080,nullptr,nullptr,instance,nullptr);
        if (!window) throw std::runtime_error("Native timing window unavailable.");
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
        Check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));
        ComPtr<ID3D11DeviceContext1> draw; Check(context.As(&draw));
        ComPtr<IDXGIDevice> dxgi; Check(device.As(&dxgi)); ComPtr<IDXGIAdapter> adapter; Check(dxgi->GetAdapter(&adapter));
        ComPtr<IDXGIFactory2> factory; Check(adapter->GetParent(IID_PPV_ARGS(&factory)));
        DXGI_SWAP_CHAIN_DESC1 desc{}; desc.Width=1920; desc.Height=1080; desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count=1;
        desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount=2; desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD; desc.Flags=DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;
        ComPtr<IDXGISwapChain1> chain; Check(factory->CreateSwapChainForHwnd(device.Get(),window,&desc,nullptr,nullptr,&chain));
        ComPtr<IDXGISwapChain2> pacing; Check(chain.As(&pacing)); Check(pacing->SetMaximumFrameLatency(1)); const auto frame_ready=pacing->GetFrameLatencyWaitableObject();
        ComPtr<ID3D11Texture2D> surface; Check(chain->GetBuffer(0,IID_PPV_ARGS(&surface))); ComPtr<ID3D11RenderTargetView> target; Check(device->CreateRenderTargetView(surface.Get(),nullptr,&target));
        for(int frame=0;frame<2400;++frame) { const auto value=static_cast<short>(700*std::sin(2*3.141592653589793*997*frame/48000)); pcm[frame*2]=value; pcm[frame*2+1]=value; }
        WAVEFORMATEX format{WAVE_FORMAT_PCM,2,48000,192000,4,16,0};
        buffer.lpData=reinterpret_cast<char*>(pcm.data()); buffer.dwBufferLength=static_cast<DWORD>(pcm.size()*sizeof(short)); buffer.dwFlags=WHDR_BEGINLOOP|WHDR_ENDLOOP; buffer.dwLoops=10000;
        if(waveOutOpen(&audio,WAVE_MAPPER,&format,0,0,CALLBACK_NULL)==MMSYSERR_NOERROR) { waveOutPrepareHeader(audio,&buffer,sizeof(buffer)); waveOutWrite(audio,&buffer,sizeof(buffer)); }
        timer=CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS); if(!timer) throw std::runtime_error("Precise timer unavailable.");
        ShowWindow(window,SW_SHOWNOACTIVATE); LARGE_INTEGER frequency{},began{}; QueryPerformanceFrequency(&frequency); QueryPerformanceCounter(&began); std::uint64_t tick=1,frames=0;
        const float white[]{1,1,1,1}, black[]{0,0,0,1}, magenta[]{1,0,1,1};
        auto rectangle=[&](RECT rect,const float* color) { draw->ClearView(target.Get(),color,&rect,1); };
        while(running) {
            MSG message{}; while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) { TranslateMessage(&message); DispatchMessageW(&message); }
            if(!running) break;
            LARGE_INTEGER now{}; QueryPerformanceCounter(&now); const auto wait=static_cast<double>(tick)/60-static_cast<double>(now.QuadPart-began.QuadPart)/frequency.QuadPart;
            if(wait>0) { LARGE_INTEGER due{}; due.QuadPart=-static_cast<LONGLONG>(wait*10000000); SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE); WaitForSingleObject(timer,100); }
            if(frame_ready) WaitForSingleObject(frame_ready,100);
            const auto clock=Clock();
            for(int row=0;row<12;++row) {
                const int phase=static_cast<int>(frames%256); const float color[]{((phase*3+row*17)%256)/255.f,((phase*7+row*31)%256)/255.f,((phase*11+row*13)%256)/255.f,1};
                RECT band{0,row*90,1920,(row+1)*90}; rectangle(band,color);
                const int x=static_cast<int>((frames*17+row*73)%2070)-150; rectangle({x,row*90+4,x+150,(row+1)*90-4},white);
            }
            rectangle({16,64,32,96},magenta); rectangle({560,64,576,96},magenta);
            for(int bit=0;bit<32;++bit) rectangle({48+bit*16,64,64+bit*16,96},clock&(1u<<bit)?white:black);
            MMTIME position{}; position.wType=TIME_SAMPLES; const bool flash=audio && waveOutGetPosition(audio,&position,sizeof(position))==MMSYSERR_NOERROR && position.wType==TIME_SAMPLES && position.u.sample%48000<2400;
            rectangle({620,64,680,124},flash?white:black);
            Check(chain->Present(0,0)); ++frames; SetPropW(window,L"JKFixturePaintCount",reinterpret_cast<HANDLE>(static_cast<ULONG_PTR>(frames))); ++tick;
            QueryPerformanceCounter(&now); const auto elapsed=static_cast<double>(now.QuadPart-began.QuadPart)/frequency.QuadPart; if(elapsed>static_cast<double>(tick)/60) tick=static_cast<std::uint64_t>(elapsed*60)+1;
        }
        if(frame_ready) CloseHandle(frame_ready);
    } catch(const std::exception& error) { OutputDebugStringA(error.what()); running=false; }
    if(audio) { waveOutReset(audio); waveOutUnprepareHeader(audio,&buffer,sizeof(buffer)); waveOutClose(audio); }
    if(timer) CloseHandle(timer); if(window) { RemovePropW(window,L"JKFixturePaintCount"); DestroyWindow(window); }
    return 0;
}
