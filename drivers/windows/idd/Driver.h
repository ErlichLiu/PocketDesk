// PocketDesk 手机屏：IddCx（间接显示）驱动的类型定义。
// 结构照搬微软 IddSampleDriver 示例，去掉 WPP 跟踪与固定 EDID 表：
// 显示器描述由 PocketDeskEdid.h 提供，模式列表按 ScreenFit 的档位在运行时算出来。
#pragma once

#define NOMINMAX
#include <windows.h>
#include <bugcodes.h>
#include <wudfwdm.h>
#include <wdf.h>
#include <iddcx.h>

#include <dxgi1_5.h>
#include <d3d11_2.h>
#include <avrt.h>
#include <wrl.h>

#include <memory>
#include <vector>

namespace Microsoft
{
    namespace WRL
    {
        namespace Wrappers
        {
            // 给 WRL 的句柄包装补一个线程句柄类型
            typedef HandleT<HandleTraits::HANDLENullTraits> Thread;
        }
    }
}

namespace PocketDesk
{
    /// 一档分辨率：宽、高与垂直刷新率。
    struct ScreenMode
    {
        DWORD Width;
        DWORD Height;
        DWORD VSync;
    };

    /// 手机屏的规格（与 PocketDeskCore 的 ScreenFit 保持一致）。
    namespace Spec
    {
        constexpr DWORD BaseWidth = 1080;   // 基准宽度：对应 macOS 版的 540 点、200% 缩放
        constexpr DWORD MaxWidth = 2160;
        constexpr DWORD Step = 20;
        constexpr DWORD VSync = 60;
        constexpr size_t ModeCount = ((MaxWidth - BaseWidth) / Step + 1) * 2;

        /// 手机竖屏：高按 540×950 的比例；整数运算避免浮点在 constexpr 里的取整差异
        constexpr DWORD Height(DWORD width) { return (width * 950 + 270) / 540; }

        /// 折叠屏展开（OPPO Find N5 内屏 2248×2480）：同一批宽度档，高度按这个比例算。
        constexpr DWORD FoldAspectW = 2248;
        constexpr DWORD FoldAspectH = 2480;
        constexpr DWORD FoldHeight(DWORD width) { return (width * FoldAspectH + FoldAspectW / 2) / FoldAspectW; }

        constexpr ScreenMode ModeAt(size_t index)
        {
            return { BaseWidth + static_cast<DWORD>(index) * Step, Height(BaseWidth + static_cast<DWORD>(index) * Step), VSync };
        }
    }

    /// 管理渲染设备：IddCx 的交换链要用到系统的 GPU。
    struct Direct3DDevice
    {
        Direct3DDevice(LUID AdapterLuid);
        Direct3DDevice();
        HRESULT Init();

        LUID AdapterLuid;
        Microsoft::WRL::ComPtr<IDXGIFactory5> DxgiFactory;
        Microsoft::WRL::ComPtr<IDXGIAdapter1> Adapter;
        Microsoft::WRL::ComPtr<ID3D11Device> Device;
        Microsoft::WRL::ComPtr<ID3D11DeviceContext> DeviceContext;
    };

    /// 消费交换链上的每一帧。
    /// 虚拟屏没有真实面板，帧不需要送去任何地方，但必须按节奏「取走并归还」，
    /// 否则系统会认为这块屏卡住，画面停在第一帧。
    class SwapChainProcessor
    {
    public:
        SwapChainProcessor(IDDCX_SWAPCHAIN hSwapChain, std::shared_ptr<Direct3DDevice> Device, HANDLE NewFrameEvent);
        ~SwapChainProcessor();

    private:
        static DWORD CALLBACK RunThread(LPVOID Argument);

        void Run();
        void RunCore();

        IDDCX_SWAPCHAIN m_hSwapChain;
        std::shared_ptr<Direct3DDevice> m_Device;
        HANDLE m_hAvailableBufferEvent;
        Microsoft::WRL::Wrappers::Thread m_hThread;
        Microsoft::WRL::Wrappers::Event m_hTerminateEvent;
    };

    /// 适配器：一块虚拟显示卡。设备启动后上报一块手机屏。
    class DeviceContext
    {
    public:
        DeviceContext(_In_ WDFDEVICE WdfDevice);
        virtual ~DeviceContext();

        void InitAdapter();
        void FinishInit();

    protected:
        WDFDEVICE m_WdfDevice;
        IDDCX_ADAPTER m_Adapter;
    };

    /// 显示器：一块手机屏。
    class MonitorContext
    {
    public:
        MonitorContext(_In_ IDDCX_MONITOR Monitor);
        virtual ~MonitorContext();

        void AssignSwapChain(IDDCX_SWAPCHAIN SwapChain, LUID RenderAdapter, HANDLE NewFrameEvent);
        void UnassignSwapChain();

    private:
        IDDCX_MONITOR m_Monitor;
        std::unique_ptr<SwapChainProcessor> m_ProcessingThread;
    };
}
