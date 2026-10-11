import { useCallback, useEffect, useState } from 'react'
import { api, getAccessToken } from '../api/client'

interface RollRoom {
  id: string
  title: string
  status: number
  startAtUtc?: string
  endAtUtc?: string
}

interface RollRoomDetail extends RollRoom {
  prizes: { id: string; title: string; imageUrl?: string; weight: number }[]
  participants: { id: string; userId: string; entryCount: number }[]
}

const STATUS_TEXT: Record<number, string> = {
  0: '未开始',
  1: '进行中',
  2: '待开奖',
  3: '开奖中',
  4: '已开奖',
  5: '已结束',
}

/** Live roll rooms: join with your token, watch the draw, claim a win. */
export default function RollRoomsPage() {
  const [rooms, setRooms] = useState<RollRoom[]>([])
  const [openRoom, setOpenRoom] = useState<RollRoomDetail | null>(null)
  const [error, setError] = useState('')

  const reload = useCallback(() => {
    api<{ items: RollRoom[] }>('/api/roll/rooms')
      .then((result) => setRooms(result.items))
      .catch((err) => setError(String(err.message ?? err)))
  }, [])

  useEffect(() => {
    reload()
    const timer = setInterval(reload, 5000)
    return () => clearInterval(timer)
  }, [reload])

  async function showDetail(id: string) {
    try {
      setOpenRoom(await api<RollRoomDetail>(`/api/roll/rooms/${id}`))
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  async function join(id: string) {
    if (!getAccessToken()) {
      window.location.hash = '#login'
      return
    }
    try {
      await api(`/api/roll/rooms/${id}/join`, { body: {} })
      await showDetail(id)
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  async function draw(id: string) {
    try {
      await api(`/api/roll/rooms/${id}/draw`, { body: {} })
      await showDetail(id)
    } catch (err) {
      setError(String((err as Error).message ?? err))
    }
  }

  return (
    <div>
      <h1 className="page-title">Roll 房</h1>
      {error && <p className="error-text">{error}</p>}
      <div className="grid cols-3">
        {rooms.map((room) => (
          <div key={room.id} className="card">
            <h3 style={{ margin: '0 0 6px', fontSize: 15 }}>{room.title}</h3>
            <p className="muted">
              {STATUS_TEXT[room.status] ?? room.status}
              {room.endAtUtc && ` · ${room.endAtUtc.slice(11, 16)} 结束`}
            </p>
            <div style={{ display: 'flex', gap: 8 }}>
              <button className="btn secondary" onClick={() => showDetail(room.id)}>
                详情
              </button>
              {room.status === 1 && (
                <button className="btn" onClick={() => join(room.id)}>
                  参与
                </button>
              )}
              {room.status === 2 && (
                <button className="btn" onClick={() => draw(room.id)}>
                  开奖
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
      {rooms.length === 0 && !error && <p className="muted">暂无进行中的房间</p>}

      {openRoom && (
        <div className="card" style={{ marginTop: 20 }}>
          <h3>{openRoom.title}</h3>
          <p className="muted">{STATUS_TEXT[openRoom.status] ?? openRoom.status}</p>
          <strong>奖品</strong>
          <table className="table">
            <tbody>
              {openRoom.prizes.map((prize) => (
                <tr key={prize.id}>
                  <td>{prize.title}</td>
                  <td className="muted">权重 {prize.weight}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <strong>参与者 {openRoom.participants.length}</strong>
        </div>
      )}
    </div>
  )
}
